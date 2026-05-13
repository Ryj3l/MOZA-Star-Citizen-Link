using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using MozaStarCitizen.App.Diagnostics;
using MozaStarCitizen.App.Models;

namespace MozaStarCitizen.App.ScreenCapture;

public sealed class ScreenVisualEventMonitor : IDisposable
{
    private static readonly TimeSpan CaptureInterval = TimeSpan.FromMilliseconds(125);
    private static readonly TimeSpan ImpactCooldown = TimeSpan.FromMilliseconds(1400);
    private static readonly TimeSpan UiTransitionSuppression = TimeSpan.FromMilliseconds(1750);
    private const int SampleWidth = 160;
    private const int SampleHeight = 90;
    private const double DefaultAverageDiffThreshold = 34;
    private const double DefaultHighDiffRatioThreshold = 0.24;
    private const double DefaultRedDominanceThreshold = 0.09;
    private const double DefaultQuietAverageDiffThreshold = 20;
    private const double DefaultQuietHighDiffRatioThreshold = 0.16;
    private const double DefaultMaxAverageDiffThreshold = 70;
    private const double DefaultMaxHighDiffRatioThreshold = 0.58;
    private const int DefaultRequiredStableFrames = 6;
    private const double DefaultAltimeterEnterScore = 70;
    private const double DefaultAltimeterExitScore = 20;
    private const int DefaultAltimeterEnterFrames = 4;
    private const int DefaultAltimeterExitFrames = 32;
    private readonly double _averageDiffThreshold = ReadDouble("MOZA_SC_SCREEN_IMPACT_DIFF", DefaultAverageDiffThreshold);
    private readonly double _highDiffRatioThreshold = ReadDouble("MOZA_SC_SCREEN_IMPACT_RATIO", DefaultHighDiffRatioThreshold);
    private readonly double _redDominanceThreshold = ReadDouble("MOZA_SC_SCREEN_RED_RATIO", DefaultRedDominanceThreshold);
    private readonly double _quietAverageDiffThreshold = ReadDouble("MOZA_SC_SCREEN_QUIET_DIFF", DefaultQuietAverageDiffThreshold);
    private readonly double _quietHighDiffRatioThreshold = ReadDouble("MOZA_SC_SCREEN_QUIET_RATIO", DefaultQuietHighDiffRatioThreshold);
    private readonly double _maxAverageDiffThreshold = ReadDouble("MOZA_SC_SCREEN_MAX_DIFF", DefaultMaxAverageDiffThreshold);
    private readonly double _maxHighDiffRatioThreshold = ReadDouble("MOZA_SC_SCREEN_MAX_RATIO", DefaultMaxHighDiffRatioThreshold);
    private readonly int _requiredStableFrames = ReadInt("MOZA_SC_SCREEN_STABLE_FRAMES", DefaultRequiredStableFrames);
    private readonly double _altimeterEnterScore = ReadDouble("MOZA_SC_SCREEN_ALTIMETER_ENTER", DefaultAltimeterEnterScore);
    private readonly double _altimeterExitScore = ReadDouble("MOZA_SC_SCREEN_ALTIMETER_EXIT", DefaultAltimeterExitScore);
    private readonly int _altimeterEnterFrames = ReadInt("MOZA_SC_SCREEN_ALTIMETER_ENTER_FRAMES", DefaultAltimeterEnterFrames);
    private readonly int _altimeterExitFrames = ReadInt("MOZA_SC_SCREEN_ALTIMETER_EXIT_FRAMES", DefaultAltimeterExitFrames);
    private CancellationTokenSource? _cts;
    private Task? _worker;
    private ScreenFrame? _previousFrame;
    private DateTimeOffset _lastImpact = DateTimeOffset.MinValue;
    private DateTimeOffset _suppressUntil = DateTimeOffset.MinValue;
    private DateTimeOffset _lastRejectionLog = DateTimeOffset.MinValue;
    private DateTimeOffset _lastAltimeterLog = DateTimeOffset.MinValue;
    private int _stableFrames;
    private int _altimeterPresentFrames;
    private int _altimeterAbsentFrames;
    private bool _atmosphereActive;
    private string _status = "Screen capture disabled.";
    private long _framesAnalyzed;
    private long _eventsDetected;

    public event EventHandler<ScGameEvent>? EventDetected;

    public event EventHandler<string>? Faulted;

    public bool IsRunning => _worker is { IsCompleted: false };

    public string Status => _status;

    public long FramesAnalyzed => Interlocked.Read(ref _framesAnalyzed);

    public long EventsDetected => Interlocked.Read(ref _eventsDetected);

    public static bool IsEnabledByEnvironment()
    {
        var value = Environment.GetEnvironmentVariable("MOZA_SC_SCREEN");
        return value is not null &&
               (value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("on", StringComparison.OrdinalIgnoreCase));
    }

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        _previousFrame = null;
        _cts = new CancellationTokenSource();
        _worker = Task.Run(() => RunAsync(_cts.Token));
    }

    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        await _cts.CancelAsync();
        if (_worker is not null)
        {
            try
            {
                await _worker;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _cts.Dispose();
        _cts = null;
        _worker = null;
        _previousFrame = null;
        SetStatus("Screen capture stopped.");
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        SetStatus("Screen capture monitor starting.");
        AppLog.Write(
            $"Screen capture impact detector enabled. avgDiff>={_averageDiffThreshold:0.###}, highDiffRatio>={_highDiffRatioThreshold:0.###}, redRatio>={_redDominanceThreshold:0.###}, maxDiff<{_maxAverageDiffThreshold:0.###}, maxRatio<{_maxHighDiffRatioThreshold:0.###}, stableFrames={_requiredStableFrames}.");
        AppLog.Write(
            $"Screen capture altimeter detector enabled. enterScore>={_altimeterEnterScore:0.###} for {_altimeterEnterFrames} frame(s), exitScore<{_altimeterExitScore:0.###} for {_altimeterExitFrames} frame(s).");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!ScreenFrameCapturer.TryCaptureStarCitizenFrame(SampleWidth, SampleHeight, out var frame, out var status))
                {
                    SetStatus(status);
                    _previousFrame = null;
                    SuppressAtmosphere("screen capture unavailable", status, immediate: true);
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                    continue;
                }

                SetStatus(status);
                Interlocked.Increment(ref _framesAnalyzed);
                AnalyzeFrame(frame);
                await Task.Delay(CaptureInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var message = $"Screen capture monitor warning: {ex.Message}";
                AppLog.Write(ex, "Screen capture monitor failed");
                Faulted?.Invoke(this, message);
                SetStatus(message);
                _previousFrame = null;
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }

    private void AnalyzeFrame(ScreenFrame frame)
    {
        AnalyzeAltimeter(frame);
        if (frame.CursorVisible)
        {
            _previousFrame = null;
            _stableFrames = 0;
            return;
        }

        var previous = _previousFrame;
        _previousFrame = frame;
        if (previous is null || previous.Luma.Length != frame.Luma.Length)
        {
            return;
        }

        double diffSum = 0;
        var highDiffPixels = 0;
        for (var i = 0; i < frame.Luma.Length; i++)
        {
            var diff = Math.Abs(frame.Luma[i] - previous.Luma[i]);
            diffSum += diff;
            if (diff >= 45)
            {
                highDiffPixels++;
            }
        }

        var pixelCount = frame.Luma.Length;
        var averageDiff = diffSum / pixelCount;
        var highDiffRatio = (double)highDiffPixels / pixelCount;
        var redDominanceDelta = frame.RedDominanceRatio - previous.RedDominanceRatio;
        var now = DateTimeOffset.Now;

        if (averageDiff <= _quietAverageDiffThreshold && highDiffRatio <= _quietHighDiffRatioThreshold)
        {
            _stableFrames = Math.Min(_requiredStableFrames, _stableFrames + 1);
        }

        var impactLikeMotion = averageDiff >= _averageDiffThreshold &&
                               highDiffRatio >= _highDiffRatioThreshold;
        var impactLikeRedFlash = frame.RedDominanceRatio >= _redDominanceThreshold &&
                                 redDominanceDelta >= 0.035 &&
                                 averageDiff >= 10;

        if (!impactLikeMotion && !impactLikeRedFlash)
        {
            return;
        }

        var uiTransitionLike = averageDiff >= _maxAverageDiffThreshold ||
                               highDiffRatio >= _maxHighDiffRatioThreshold;
        if (uiTransitionLike)
        {
            _stableFrames = 0;
            _suppressUntil = now + UiTransitionSuppression;
            LogRejectedCandidate("likely UI/menu transition", averageDiff, highDiffRatio, frame.RedDominanceRatio);
            return;
        }

        if (now < _suppressUntil)
        {
            LogRejectedCandidate("recent UI/menu transition", averageDiff, highDiffRatio, frame.RedDominanceRatio);
            return;
        }

        if (_stableFrames < _requiredStableFrames)
        {
            LogRejectedCandidate($"waiting for stable gameplay frames ({_stableFrames}/{_requiredStableFrames})", averageDiff, highDiffRatio, frame.RedDominanceRatio);
            return;
        }

        if (now - _lastImpact < ImpactCooldown)
        {
            return;
        }

        _stableFrames = 0;
        _lastImpact = now;
        var eventNumber = Interlocked.Increment(ref _eventsDetected);
        var intensity = ScaleImpactIntensity(averageDiff, highDiffRatio, frame.RedDominanceRatio);
        var source = $"screen-capture impact avgDiff={averageDiff:0.###} highDiffRatio={highDiffRatio:0.###} redRatio={frame.RedDominanceRatio:0.###} source={frame.Source}";
        AppLog.Write($"Screen capture detected impact candidate #{eventNumber}: {source}");

        EventDetected?.Invoke(
            this,
            new ScGameEvent(
                ScEventKind.LandingImpact,
                "Screen impact candidate",
                intensity,
                TimeSpan.FromMilliseconds(180),
                source,
                now));
    }

    private void AnalyzeAltimeter(ScreenFrame frame)
    {
        var score = frame.CursorVisible ? 0 : frame.AltimeterScore;
        var now = DateTimeOffset.Now;

        if (frame.CursorVisible)
        {
            _altimeterPresentFrames = 0;
            _altimeterAbsentFrames = _altimeterExitFrames;
        }
        else if (score >= _altimeterEnterScore)
        {
            _altimeterPresentFrames = Math.Min(_altimeterEnterFrames, _altimeterPresentFrames + 1);
            _altimeterAbsentFrames = 0;
        }
        else if (score < _altimeterExitScore)
        {
            _altimeterAbsentFrames = Math.Min(_altimeterExitFrames, _altimeterAbsentFrames + 1);
            _altimeterPresentFrames = 0;
        }

        if (now - _lastAltimeterLog >= TimeSpan.FromSeconds(5))
        {
            _lastAltimeterLog = now;
            AppLog.Write($"Screen capture altimeter score={score:0.###}; rawScore={frame.AltimeterScore:0.###}; presentFrames={_altimeterPresentFrames}; absentFrames={_altimeterAbsentFrames}; atmosphereActive={_atmosphereActive}; cursorVisible={frame.CursorVisible}; detail={frame.AltimeterDetail}; source={frame.Source}.");
        }

        if (!_atmosphereActive && _altimeterPresentFrames >= _altimeterEnterFrames)
        {
            _atmosphereActive = true;
            AppLog.Write($"Screen capture detected altimeter; starting atmosphere rumble. score={score:0.###}; source={frame.Source}");
            EventDetected?.Invoke(
                this,
                new ScGameEvent(
                    ScEventKind.AtmosphereEntered,
                    "Screen altimeter detected",
                    0.28,
                    TimeSpan.Zero,
                    $"screen-capture altimeter score={score:0.###} detail={frame.AltimeterDetail} source={frame.Source}",
                    now));
        }
        else if (_atmosphereActive && _altimeterAbsentFrames >= _altimeterExitFrames)
        {
            _atmosphereActive = false;
            AppLog.Write($"Screen capture lost altimeter; stopping atmosphere rumble. score={score:0.###}; detail={frame.AltimeterDetail}; source={frame.Source}");
            EventDetected?.Invoke(
                this,
                new ScGameEvent(
                    ScEventKind.AtmosphereExited,
                    "Screen altimeter lost",
                    0,
                    TimeSpan.Zero,
                    $"screen-capture altimeter score={score:0.###} detail={frame.AltimeterDetail} source={frame.Source}",
                    now));
        }
    }

    private void SuppressAtmosphere(string title, string source, bool immediate)
    {
        _altimeterPresentFrames = 0;
        _altimeterAbsentFrames = immediate
            ? _altimeterExitFrames
            : Math.Min(_altimeterExitFrames, _altimeterAbsentFrames + 1);

        if (!_atmosphereActive || _altimeterAbsentFrames < _altimeterExitFrames)
        {
            return;
        }

        var now = DateTimeOffset.Now;
        _atmosphereActive = false;
        AppLog.Write($"Screen capture stopped atmosphere rumble: {title}; source={source}");
        EventDetected?.Invoke(
            this,
            new ScGameEvent(
                ScEventKind.AtmosphereExited,
                "Screen altimeter unavailable",
                0,
                TimeSpan.Zero,
                $"screen-capture altimeter unavailable reason={title} source={source}",
                now));
    }

    private void LogRejectedCandidate(string reason, double averageDiff, double highDiffRatio, double redDominanceRatio)
    {
        var now = DateTimeOffset.Now;
        if (now - _lastRejectionLog < TimeSpan.FromSeconds(2))
        {
            return;
        }

        _lastRejectionLog = now;
        AppLog.Write($"Screen capture rejected impact candidate: {reason}; avgDiff={averageDiff:0.###} highDiffRatio={highDiffRatio:0.###} redRatio={redDominanceRatio:0.###}.");
    }

    private void SetStatus(string status)
    {
        if (string.Equals(_status, status, StringComparison.Ordinal))
        {
            return;
        }

        _status = status;
        AppLog.Write($"Screen capture status: {status}");
    }

    private static double ScaleImpactIntensity(double averageDiff, double highDiffRatio, double redDominanceRatio)
    {
        var diffComponent = Math.Clamp((averageDiff - 18) / 56, 0, 1);
        var motionComponent = Math.Clamp(highDiffRatio / 0.45, 0, 1);
        var redComponent = Math.Clamp(redDominanceRatio / 0.18, 0, 1);
        return Math.Clamp(0.28 + (diffComponent * 0.34) + (motionComponent * 0.24) + (redComponent * 0.14), 0.25, 0.9);
    }

    private static double ReadDouble(string name, double fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return double.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : fallback;
    }

    private static int ReadInt(string name, int fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return int.TryParse(value, out var parsed) && parsed >= 0
            ? parsed
            : fallback;
    }

    public void Dispose()
    {
        _ = StopAsync();
    }

    private sealed record ScreenFrame(
        byte[] Luma,
        double RedDominanceRatio,
        double AltimeterScore,
        string AltimeterDetail,
        bool CursorVisible,
        string Source);

    private sealed record AltimeterDetection(double Score, string Detail);

    private static class ScreenFrameCapturer
    {
        private const int SmXVirtualScreen = 76;
        private const int SmYVirtualScreen = 77;
        private const int SmCxVirtualScreen = 78;
        private const int SmCyVirtualScreen = 79;

        public static bool TryCaptureStarCitizenFrame(
            int sampleWidth,
            int sampleHeight,
            out ScreenFrame frame,
            out string status)
        {
            frame = new ScreenFrame([], 0, 0, string.Empty, false, string.Empty);

            if (!TryFindStarCitizenWindow(out var bounds, out var source))
            {
                status = "Screen capture waiting for a visible StarCitizen window.";
                return false;
            }

            if (bounds.Width < 320 || bounds.Height < 200)
            {
                status = $"Screen capture skipped small StarCitizen window {bounds.Width}x{bounds.Height}.";
                return false;
            }

            try
            {
                var cursorVisible = IsCursorVisibleWithin(bounds);
                frame = Capture(bounds, sampleWidth, sampleHeight, source, cursorVisible);
                status = cursorVisible
                    ? $"Screen capture active: {source} {bounds.Width}x{bounds.Height}; cursor visible, screen-triggered effects suppressed."
                    : $"Screen capture active: {source} {bounds.Width}x{bounds.Height}.";
                return true;
            }
            catch (Exception ex) when (ex is ExternalException or ArgumentException or InvalidOperationException)
            {
                status = $"Screen capture failed: {ex.Message}";
                return false;
            }
        }

        private static ScreenFrame Capture(Rectangle bounds, int sampleWidth, int sampleHeight, string source, bool cursorVisible)
        {
            using var full = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(full))
            {
                graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
            }

            var altimeter = cursorVisible
                ? new AltimeterDetection(0, "cursor visible")
                : AltimeterDetector.Score(full);

            using var sample = new Bitmap(sampleWidth, sampleHeight, PixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(sample))
            {
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                graphics.DrawImage(full, new Rectangle(0, 0, sampleWidth, sampleHeight));
            }

            return BuildFrame(sample, altimeter, cursorVisible, source);
        }

        private static ScreenFrame BuildFrame(Bitmap bitmap, AltimeterDetection altimeter, bool cursorVisible, string source)
        {
            var width = bitmap.Width;
            var height = bitmap.Height;
            var luma = new byte[width * height];
            var redDominantPixels = 0;
            var data = bitmap.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format24bppRgb);

            try
            {
                unsafe
                {
                    var basePointer = (byte*)data.Scan0;
                    for (var y = 0; y < height; y++)
                    {
                        var row = basePointer + (y * data.Stride);
                        for (var x = 0; x < width; x++)
                        {
                            var pixel = row + (x * 3);
                            var blue = pixel[0];
                            var green = pixel[1];
                            var red = pixel[2];
                            luma[(y * width) + x] = (byte)((red * 0.299) + (green * 0.587) + (blue * 0.114));
                            if (red >= 135 && red >= green * 1.25 && red >= blue * 1.25)
                            {
                                redDominantPixels++;
                            }
                        }
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return new ScreenFrame(luma, (double)redDominantPixels / luma.Length, altimeter.Score, altimeter.Detail, cursorVisible, source);
        }

        private static bool TryFindStarCitizenWindow(out Rectangle bounds, out string source)
        {
            if (TryGetStarCitizenWindow(GetForegroundWindow(), out bounds, out source))
            {
                source = $"foreground {source}";
                return true;
            }

            bounds = Rectangle.Empty;
            source = string.Empty;
            return false;
        }

        private static bool TryGetStarCitizenWindow(IntPtr hwnd, out Rectangle bounds, out string source)
        {
            bounds = Rectangle.Empty;
            source = string.Empty;
            if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd))
            {
                return false;
            }

            _ = GetWindowThreadProcessId(hwnd, out var processId);
            if (processId == 0)
            {
                return false;
            }

            Process process;
            try
            {
                process = Process.GetProcessById((int)processId);
            }
            catch (ArgumentException)
            {
                return false;
            }

            using (process)
            {
                if (!string.Equals(process.ProcessName, "StarCitizen", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (!GetWindowRect(hwnd, out var rect))
                {
                    return false;
                }

                var windowBounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                var virtualScreen = GetVirtualScreenBounds();
                windowBounds.Intersect(virtualScreen);
                if (windowBounds.IsEmpty)
                {
                    return false;
                }

                bounds = windowBounds;
                source = $"{process.ProcessName} window";
                var title = GetWindowTitle(hwnd);
                if (!string.IsNullOrWhiteSpace(title))
                {
                    source = $"{source} '{title}'";
                }

                return true;
            }
        }

        private static Rectangle GetVirtualScreenBounds() =>
            new(
                GetSystemMetrics(SmXVirtualScreen),
                GetSystemMetrics(SmYVirtualScreen),
                GetSystemMetrics(SmCxVirtualScreen),
                GetSystemMetrics(SmCyVirtualScreen));

        private static string GetWindowTitle(IntPtr hwnd)
        {
            var length = GetWindowTextLength(hwnd);
            if (length <= 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder(length + 1);
            _ = GetWindowText(hwnd, builder, builder.Capacity);
            return builder.ToString();
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        private static extern bool GetCursorInfo(ref CursorInfo pci);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct NativeRect
        {
            public readonly int Left;
            public readonly int Top;
            public readonly int Right;
            public readonly int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CursorInfo
        {
            public int Size;
            public int Flags;
            public IntPtr Cursor;
            public NativePoint ScreenPosition;
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct NativePoint
        {
            public readonly int X;
            public readonly int Y;
        }

        private static bool IsCursorVisibleWithin(Rectangle bounds)
        {
            const int cursorShowing = 0x00000001;
            var cursorInfo = new CursorInfo
            {
                Size = Marshal.SizeOf<CursorInfo>()
            };

            if (!GetCursorInfo(ref cursorInfo) ||
                (cursorInfo.Flags & cursorShowing) == 0)
            {
                return false;
            }

            return bounds.Contains(cursorInfo.ScreenPosition.X, cursorInfo.ScreenPosition.Y);
        }

        private static class AltimeterDetector
        {
            private const int SampleWidth = 240;
            private const int SampleHeight = 300;
            private static readonly SearchRegion[] SearchRegions =
            [
                new("altimeter boxed value", 0.535, 0.420, 0.095, 0.150),
                new("altimeter boxed value tight", 0.545, 0.430, 0.080, 0.135),
                new("altimeter boxed value shifted", 0.525, 0.405, 0.110, 0.180)
            ];

            public static AltimeterDetection Score(Bitmap fullFrame)
            {
                var best = new AltimeterDetection(0, "no matching region");
                foreach (var region in SearchRegions)
                {
                    var source = ToRectangle(fullFrame, region);
                    if (source.Width <= 0 || source.Height <= 0)
                    {
                        continue;
                    }

                    using var sample = new Bitmap(SampleWidth, SampleHeight, PixelFormat.Format24bppRgb);
                    using (var graphics = Graphics.FromImage(sample))
                    {
                        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                        graphics.PixelOffsetMode = PixelOffsetMode.Half;
                        graphics.DrawImage(fullFrame, new Rectangle(0, 0, SampleWidth, SampleHeight), source, GraphicsUnit.Pixel);
                    }

                    var detection = ScoreSample(sample, region.Name);
                    if (detection.Score > best.Score)
                    {
                        best = detection;
                    }
                }

                return best;
            }

            private static Rectangle ToRectangle(Bitmap bitmap, SearchRegion region)
            {
                var x = (int)Math.Round(bitmap.Width * region.X);
                var y = (int)Math.Round(bitmap.Height * region.Y);
                var width = (int)Math.Round(bitmap.Width * region.Width);
                var height = (int)Math.Round(bitmap.Height * region.Height);
                var rectangle = new Rectangle(x, y, width, height);
                rectangle.Intersect(new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                return rectangle;
            }

            private static AltimeterDetection ScoreSample(Bitmap bitmap, string regionName)
            {
                var width = bitmap.Width;
                var height = bitmap.Height;
                var masks = ReadHudMasks(bitmap);
                var total = AnalyzeComponents(masks.Any, width, height, new Rectangle(0, 0, width, height));
                var unit = AnalyzeComponents(masks.Any, width, height, ToSampleRectangle(width, height, 0.31, 0.04, 0.36, 0.22));
                var scale = AnalyzeComponents(masks.Any, width, height, ToSampleRectangle(width, height, 0.18, 0.17, 0.30, 0.78));
                var value = AnalyzeComponents(masks.Any, width, height, ToSampleRectangle(width, height, 0.35, 0.44, 0.34, 0.22));
                var neutralValue = AnalyzeComponents(masks.Neutral, width, height, ToSampleRectangle(width, height, 0.35, 0.44, 0.34, 0.22));
                var box = AnalyzeBox(masks.Any, width, height, ToSampleRectangle(width, height, 0.30, 0.39, 0.46, 0.32));

                var boxLooksValid = box.EdgeCount >= 3 &&
                                    box.HorizontalEdgeCount >= 1 &&
                                    box.VerticalEdgeCount >= 1 &&
                                    box.Score >= 35;
                var valueLooksNumeric = value.PixelCount >= 24 &&
                                        value.RowGroupCount >= 2 &&
                                        value.ColumnGroupCount >= 3 &&
                                        value.LineLikeCount <= 12 &&
                                        (value.DigitLikeCount >= 1 || value.TinyDotCount >= 1 || neutralValue.PixelCount >= 12);
                var scaleLooksPresent = scale.PixelCount is >= 60 and <= 2200 &&
                                        scale.RowGroupCount is >= 4 and <= 14 &&
                                        scale.ColumnGroupCount is >= 2 and <= 9 &&
                                        scale.LineLikeCount <= 18;
                var unitLooksPresent = unit.PixelCount is >= 16 and <= 900 &&
                                       unit.RowGroupCount is >= 2 and <= 7 &&
                                       unit.ColumnGroupCount is >= 2 and <= 12 &&
                                       unit.LineLikeCount <= 6;
                var cluttered = total.PixelCount > 5200 ||
                                total.LineLikeCount > 45 ||
                                value.PixelCount > 900 ||
                                scale.PixelCount > 2400;
                var hasAltimeterLayout = !cluttered &&
                                          boxLooksValid &&
                                          valueLooksNumeric &&
                                          scaleLooksPresent &&
                                          unitLooksPresent;

                var score = hasAltimeterLayout
                    ? 50 +
                      box.Score +
                      Math.Min(28, value.PixelCount / 4.0) +
                      (value.ColumnGroupCount * 1.5) +
                      (scaleLooksPresent ? 22 : 0) +
                      (unitLooksPresent ? 8 : 0)
                    : Math.Min(18, (box.Score * 0.35) + (value.PixelCount / 12.0) + (scale.PixelCount / 35.0));

                score = Math.Clamp(score, 0, 180);
                var detail =
                    $"region={regionName} layout={hasAltimeterLayout} cluttered={cluttered} " +
                    $"box={box.Compact} value={value.Compact} scale={scale.Compact} unit={unit.Compact} total={total.Compact}";
                return new AltimeterDetection(score, detail);
            }

            private static HudMasks ReadHudMasks(Bitmap bitmap)
            {
                var width = bitmap.Width;
                var height = bitmap.Height;
                var red = new byte[width * height];
                var green = new byte[width * height];
                var blue = new byte[width * height];
                var luma = new byte[width * height];
                var data = bitmap.LockBits(
                    new Rectangle(0, 0, width, height),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format24bppRgb);

                try
                {
                    unsafe
                    {
                        var basePointer = (byte*)data.Scan0;
                        for (var y = 0; y < height; y++)
                        {
                            var row = basePointer + (y * data.Stride);
                            for (var x = 0; x < width; x++)
                            {
                                var pixel = row + (x * 3);
                                var index = (y * width) + x;
                                blue[index] = pixel[0];
                                green[index] = pixel[1];
                                red[index] = pixel[2];
                                luma[index] = (byte)((pixel[2] * 0.299) + (pixel[1] * 0.587) + (pixel[0] * 0.114));
                            }
                        }
                    }
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }

                var neutral = new bool[width * height];
                var any = new bool[width * height];
                for (var y = 1; y < height - 1; y++)
                {
                    for (var x = 1; x < width - 1; x++)
                    {
                        var i = (y * width) + x;
                        var lumaGradient =
                            Math.Abs(luma[i] - luma[i - 1]) +
                            Math.Abs(luma[i] - luma[i + 1]) +
                            Math.Abs(luma[i] - luma[i - width]) +
                            Math.Abs(luma[i] - luma[i + width]);
                        var channelGradient =
                            ChannelDiff(red, green, blue, i, i - 1) +
                            ChannelDiff(red, green, blue, i, i + 1) +
                            ChannelDiff(red, green, blue, i, i - width) +
                            ChannelDiff(red, green, blue, i, i + width);
                        var edgeLike = lumaGradient >= 24 || channelGradient >= 96;
                        var minChannel = Math.Min(red[i], Math.Min(green[i], blue[i]));
                        var maxChannel = Math.Max(red[i], Math.Max(green[i], blue[i]));
                        var neutralStroke = luma[i] >= 145 &&
                                            maxChannel - minChannel <= 68 &&
                                            edgeLike;
                        var cyanStroke = luma[i] >= 75 &&
                                         blue[i] >= 95 &&
                                         green[i] >= 85 &&
                                         blue[i] >= red[i] + 20 &&
                                         green[i] >= red[i] + 8 &&
                                         (edgeLike || maxChannel - minChannel >= 80);
                        var amberStroke = luma[i] >= 80 &&
                                          red[i] >= 120 &&
                                          green[i] >= 65 &&
                                          red[i] >= blue[i] + 34 &&
                                          green[i] >= blue[i] + 8 &&
                                          red[i] >= green[i] - 24 &&
                                          (edgeLike || maxChannel - minChannel >= 80);

                        neutral[i] = neutralStroke;
                        any[i] = neutralStroke || cyanStroke || amberStroke;
                    }
                }

                return new HudMasks(neutral, any);
            }

            private static int ChannelDiff(byte[] red, byte[] green, byte[] blue, int a, int b) =>
                Math.Abs(red[a] - red[b]) +
                Math.Abs(green[a] - green[b]) +
                Math.Abs(blue[a] - blue[b]);

            private static BoxEvidence AnalyzeBox(bool[] mask, int width, int height, Rectangle box)
            {
                var horizontalBandHeight = Math.Max(6, box.Height / 5);
                var verticalBandWidth = Math.Max(6, box.Width / 7);
                var topBand = new Rectangle(box.Left, box.Top, box.Width, horizontalBandHeight);
                var bottomBand = new Rectangle(box.Left, box.Bottom - horizontalBandHeight, box.Width, horizontalBandHeight);
                var leftBand = new Rectangle(box.Left, box.Top, verticalBandWidth, box.Height);
                var rightBand = new Rectangle(box.Right - verticalBandWidth, box.Top, verticalBandWidth, box.Height);

                var top = MaxRowHits(mask, width, topBand);
                var bottom = MaxRowHits(mask, width, bottomBand);
                var left = MaxColumnHits(mask, width, height, leftBand);
                var right = MaxColumnHits(mask, width, height, rightBand);
                var horizontalThreshold = Math.Max(14, (int)Math.Round(box.Width * 0.20));
                var verticalThreshold = Math.Max(10, (int)Math.Round(box.Height * 0.16));
                var hasTop = top >= horizontalThreshold;
                var hasBottom = bottom >= horizontalThreshold;
                var hasLeft = left >= verticalThreshold;
                var hasRight = right >= verticalThreshold;
                var horizontalEdges = (hasTop ? 1 : 0) + (hasBottom ? 1 : 0);
                var verticalEdges = (hasLeft ? 1 : 0) + (hasRight ? 1 : 0);
                var edgeCount = horizontalEdges + verticalEdges;
                var normalized =
                    Math.Min(8, top / (double)horizontalThreshold * 2.5) +
                    Math.Min(8, bottom / (double)horizontalThreshold * 2.5) +
                    Math.Min(8, left / (double)verticalThreshold * 2.5) +
                    Math.Min(8, right / (double)verticalThreshold * 2.5);
                var score = (edgeCount * 14.0) + normalized;
                if (edgeCount < 3)
                {
                    score = Math.Min(score, 18);
                }

                return new BoxEvidence(top, bottom, left, right, horizontalEdges, verticalEdges, edgeCount, score);
            }

            private static int MaxRowHits(bool[] mask, int width, Rectangle region)
            {
                var max = 0;
                for (var y = region.Top; y < region.Bottom; y++)
                {
                    var hits = 0;
                    for (var x = region.Left; x < region.Right; x++)
                    {
                        if (mask[(y * width) + x])
                        {
                            hits++;
                        }
                    }

                    max = Math.Max(max, hits);
                }

                return max;
            }

            private static int MaxColumnHits(bool[] mask, int width, int height, Rectangle region)
            {
                _ = height;
                var max = 0;
                for (var x = region.Left; x < region.Right; x++)
                {
                    var hits = 0;
                    for (var y = region.Top; y < region.Bottom; y++)
                    {
                        if (mask[(y * width) + x])
                        {
                            hits++;
                        }
                    }

                    max = Math.Max(max, hits);
                }

                return max;
            }

            private static ComponentAnalysis AnalyzeComponents(bool[] mask, int width, int height, Rectangle bounds)
            {
                _ = height;
                var seen = new bool[mask.Length];
                var rowHits = new int[bounds.Height];
                var columnHits = new int[bounds.Width];
                var digitLikeCount = 0;
                var tinyDotCount = 0;
                var lineLikeCount = 0;
                var pixelCount = 0;

                for (var y = bounds.Top; y < bounds.Bottom; y++)
                {
                    for (var x = bounds.Left; x < bounds.Right; x++)
                    {
                        var index = (y * width) + x;
                        if (!mask[index])
                        {
                            continue;
                        }

                        pixelCount++;
                        rowHits[y - bounds.Top]++;
                        columnHits[x - bounds.Left]++;
                    }
                }

                var queue = new Queue<int>();
                for (var sy = bounds.Top; sy < bounds.Bottom; sy++)
                {
                    for (var sx = bounds.Left; sx < bounds.Right; sx++)
                    {
                        var startIndex = (sy * width) + sx;
                        if (!mask[startIndex] || seen[startIndex])
                        {
                            continue;
                        }

                        var minX = sx;
                        var maxX = sx;
                        var minY = sy;
                        var maxY = sy;
                        var count = 0;
                        queue.Enqueue(startIndex);
                        seen[startIndex] = true;

                        while (queue.Count > 0)
                        {
                            var index = queue.Dequeue();
                            var x = index % width;
                            var y = index / width;
                            count++;
                            minX = Math.Min(minX, x);
                            maxX = Math.Max(maxX, x);
                            minY = Math.Min(minY, y);
                            maxY = Math.Max(maxY, y);

                            EnqueueIfStroke(index - 1, x > bounds.Left);
                            EnqueueIfStroke(index + 1, x < bounds.Right - 1);
                            EnqueueIfStroke(index - width, y > bounds.Top);
                            EnqueueIfStroke(index + width, y < bounds.Bottom - 1);
                        }

                        var componentWidth = maxX - minX + 1;
                        var componentHeight = maxY - minY + 1;
                        if (count >= 5 &&
                            componentWidth is >= 2 and <= 42 &&
                            componentHeight is >= 5 and <= 62 &&
                            componentWidth / (double)componentHeight <= 2.8)
                        {
                            digitLikeCount++;
                        }

                        if (count is >= 2 and <= 12 &&
                            componentWidth <= 8 &&
                            componentHeight <= 8)
                        {
                            tinyDotCount++;
                        }

                        if (count >= 10 &&
                            ((componentWidth >= 22 && componentHeight <= 7) ||
                             (componentHeight >= 22 && componentWidth <= 7)))
                        {
                            lineLikeCount++;
                        }

                        void EnqueueIfStroke(int candidate, bool inBounds)
                        {
                            if (!inBounds || !mask[candidate] || seen[candidate])
                            {
                                return;
                            }

                            seen[candidate] = true;
                            queue.Enqueue(candidate);
                        }
                    }
                }

                return new ComponentAnalysis(
                    pixelCount,
                    digitLikeCount,
                    CountHitGroups(rowHits, Math.Max(2, bounds.Width / 48)),
                    CountHitGroups(columnHits, Math.Max(2, bounds.Height / 64)),
                    tinyDotCount,
                    lineLikeCount);
            }

            private static int CountHitGroups(int[] hits, int minHits)
            {
                var groups = 0;
                var inGroup = false;
                foreach (var hit in hits)
                {
                    if (hit >= minHits)
                    {
                        if (!inGroup)
                        {
                            groups++;
                        }

                        inGroup = true;
                    }
                    else
                    {
                        inGroup = false;
                    }
                }

                return groups;
            }

            private static Rectangle ToSampleRectangle(int width, int height, double x, double y, double regionWidth, double regionHeight)
            {
                var rectangle = new Rectangle(
                    (int)Math.Round(width * x),
                    (int)Math.Round(height * y),
                    (int)Math.Round(width * regionWidth),
                    (int)Math.Round(height * regionHeight));
                rectangle.Intersect(new Rectangle(0, 0, width, height));
                return rectangle;
            }

            private sealed record SearchRegion(string Name, double X, double Y, double Width, double Height);

            private sealed record HudMasks(bool[] Neutral, bool[] Any);

            private sealed record ComponentAnalysis(
                int PixelCount,
                int DigitLikeCount,
                int RowGroupCount,
                int ColumnGroupCount,
                int TinyDotCount,
                int LineLikeCount)
            {
                public string Compact =>
                    $"px={PixelCount},digits={DigitLikeCount},rows={RowGroupCount},cols={ColumnGroupCount},dots={TinyDotCount},lines={LineLikeCount}";
            }

            private sealed record BoxEvidence(
                int TopPixels,
                int BottomPixels,
                int LeftPixels,
                int RightPixels,
                int HorizontalEdgeCount,
                int VerticalEdgeCount,
                int EdgeCount,
                double Score)
            {
                public string Compact =>
                    $"score={Score:0.#},edges={EdgeCount},h={HorizontalEdgeCount},v={VerticalEdgeCount},top={TopPixels},bottom={BottomPixels},left={LeftPixels},right={RightPixels}";
            }
        }
    }
}
