using System.IO;
using System.Runtime.InteropServices;
using Moza.ScLink.Core;
using Moza.ScLink.Core.Models;

namespace Moza.ScLink.Diagnostics.MozaSdkProbe;

public sealed class MozaSdkForceFeedbackDevice : IForceFeedbackDevice
{
    private readonly string _bridgePath;
    private nint _libraryHandle;
    private MozaBridgeInitialize? _initialize;
    private MozaBridgePlayEffect? _playEffect;
    private MozaBridgeStopEffect? _stopEffect;
    private MozaBridgeStopAll? _stopAll;

    private MozaSdkForceFeedbackDevice(string bridgePath)
    {
        _bridgePath = bridgePath;
    }

    public string Name => "MOZA SDK bridge";

    public string Status => "MOZA SDK bridge detected.";

    public static IForceFeedbackDevice? CreateIfAvailable()
    {
        var bridgePath = Path.Combine(AppContext.BaseDirectory, "drivers", "MozaForceBridge.dll");
        return File.Exists(bridgePath) ? new MozaSdkForceFeedbackDevice(bridgePath) : null;
    }

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_libraryHandle != 0)
        {
            return Task.CompletedTask;
        }

        if (!NativeLibrary.TryLoad(_bridgePath, out _libraryHandle))
        {
            throw new InvalidOperationException($"Could not load MOZA bridge at '{_bridgePath}'.");
        }

        _initialize = LoadRequired<MozaBridgeInitialize>("MozaBridge_Initialize");
        _playEffect = LoadRequired<MozaBridgePlayEffect>("MozaBridge_PlayEffect");
        _stopEffect = LoadRequired<MozaBridgeStopEffect>("MozaBridge_StopEffect");
        _stopAll = LoadRequired<MozaBridgeStopAll>("MozaBridge_StopAll");

        ThrowIfFailed(_initialize(), "MOZA bridge initialization failed");
        return Task.CompletedTask;
    }

    public Task PrepareAsync(IEnumerable<ForceEffect> effects, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task PlayAsync(ForceEffect effect, CancellationToken cancellationToken)
    {
        EnsureLoaded();
        var durationMs = effect.Duration == TimeSpan.Zero
            ? 0
            : (int)Math.Clamp(effect.Duration.TotalMilliseconds, 0, int.MaxValue);

        ThrowIfFailed(
            _playEffect!((int)effect.Kind, effect.Intensity, effect.FrequencyHz, durationMs, effect.StateKey),
            $"MOZA bridge failed to play '{effect.Name}'");
        return Task.CompletedTask;
    }

    public Task StopAsync(string stateKey, CancellationToken cancellationToken)
    {
        EnsureLoaded();
        ThrowIfFailed(_stopEffect!(stateKey), $"MOZA bridge failed to stop '{stateKey}'");
        return Task.CompletedTask;
    }

    public Task StopAllAsync(CancellationToken cancellationToken)
    {
        if (_libraryHandle == 0 || _stopAll is null)
        {
            return Task.CompletedTask;
        }

        ThrowIfFailed(_stopAll(), "MOZA bridge failed to stop all effects");
        return Task.CompletedTask;
    }

    private T LoadRequired<T>(string exportName)
        where T : Delegate
    {
        if (!NativeLibrary.TryGetExport(_libraryHandle, exportName, out var exportAddress))
        {
            throw new MissingMethodException($"The MOZA bridge is missing export '{exportName}'.");
        }

        return Marshal.GetDelegateForFunctionPointer<T>(exportAddress);
    }

    private void EnsureLoaded()
    {
        if (_libraryHandle == 0 || _playEffect is null || _stopEffect is null || _stopAll is null)
        {
            throw new InvalidOperationException("MOZA bridge has not been initialized.");
        }
    }

    private static void ThrowIfFailed(int result, string message)
    {
        if (result != 0)
        {
            throw new InvalidOperationException($"{message}. Bridge result: {result}.");
        }
    }

    ~MozaSdkForceFeedbackDevice()
    {
        if (_libraryHandle != 0)
        {
            NativeLibrary.Free(_libraryHandle);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int MozaBridgeInitialize();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private delegate int MozaBridgePlayEffect(
        int effectKind,
        double intensity,
        double frequencyHz,
        int durationMs,
        [MarshalAs(UnmanagedType.LPWStr)] string? stateKey);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private delegate int MozaBridgeStopEffect([MarshalAs(UnmanagedType.LPWStr)] string stateKey);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int MozaBridgeStopAll();
}
