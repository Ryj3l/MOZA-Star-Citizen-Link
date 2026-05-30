using System.IO;
using System.Text;
using Moza.ScLink.Core.Diagnostics;

namespace Moza.ScLink.Logs;

public sealed class GameLogTailer : IDisposable
{
    private readonly string _path;
    private CancellationTokenSource? _cts;
    private Task? _worker;

    public GameLogTailer(string path)
    {
        _path = path;
    }

    public event EventHandler<string>? LineRead;

    public event EventHandler<string>? Faulted;

    public bool IsRunning => _worker is { IsCompleted: false };

    public void Start(bool startAtEnd = true)
    {
        if (IsRunning)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _worker = Task.Run(() => ReadLoopAsync(startAtEnd, _cts.Token));
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
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
                // Bound the worker drain by the caller's deadline. If the caller's CT fires before
                // the worker observes our _cts (e.g., FileStream open stalled by AV scan), surface
                // OCE so the caller (LogSensor) can log + dispose without throwing "stop failed"
                // up to the host. (#73)
                await _worker.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Worker exited via our _cts cancellation; expected.
            }
        }

        _cts.Dispose();
        _cts = null;
        _worker = null;
    }

    private async Task ReadLoopAsync(bool startAtEnd, CancellationToken cancellationToken)
    {
        var position = startAtEnd ? GetLengthOrZero() : 0L;
        var lastObservedLength = -1L;
        var lengthObservationsLogged = 0;
        AppLog.Write($"Game.log tailer starting for '{_path}' at byte position {position}.");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    await Task.Delay(1000, cancellationToken);
                    continue;
                }

                var length = new FileInfo(_path).Length;
                if (length != lastObservedLength)
                {
                    if (lengthObservationsLogged < 20 || lengthObservationsLogged % 100 == 0)
                    {
                        AppLog.Write($"Game.log tailer observed length {length} bytes for '{_path}' at byte position {position}.");
                    }

                    lastObservedLength = length;
                    lengthObservationsLogged++;
                }

                if (position > length)
                {
                    AppLog.Write($"Game.log tailer detected truncation or replacement for '{_path}'. Previous byte position {position}, new length {length}. Restarting at beginning.");
                    position = 0;
                }

                var previousPosition = position;
                await using var stream = new FileStream(
                    _path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                stream.Seek(position, SeekOrigin.Begin);

                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                while (await reader.ReadLineAsync(cancellationToken) is { } line)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        LineRead?.Invoke(this, line);
                    }
                }

                position = stream.Position;
                if (position > previousPosition)
                {
                    AppLog.Write($"Game.log tailer advanced from byte {previousPosition} to {position} for '{_path}'.");
                }

                await Task.Delay(250, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Faulted?.Invoke(this, ex.Message);
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    private long GetLengthOrZero()
    {
        try
        {
            return File.Exists(_path) ? new FileInfo(_path).Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    public void Dispose()
    {
        _ = StopAsync();
    }
}
