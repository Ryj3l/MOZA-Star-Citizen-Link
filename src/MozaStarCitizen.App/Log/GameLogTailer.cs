using System.IO;
using System.Text;

namespace MozaStarCitizen.App.Log;

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
    }

    private async Task ReadLoopAsync(bool startAtEnd, CancellationToken cancellationToken)
    {
        var position = startAtEnd ? GetLengthOrZero() : 0L;

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
                if (position > length)
                {
                    position = 0;
                }

                await using var stream = new FileStream(
                    _path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                stream.Seek(position, SeekOrigin.Begin);

                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync(cancellationToken);
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        LineRead?.Invoke(this, line);
                    }
                }

                position = stream.Position;
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
