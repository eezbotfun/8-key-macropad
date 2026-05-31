using System.IO.Pipes;
using System.Text;

namespace HardwareMonitor.Windows.Ezbf;

internal sealed class EzbfPipeClient : IDisposable
{
    private const byte ProtocolVersion = 1;
    private const byte MessageTypeEvent = 0x20;

    private readonly string _pipeName;
    private readonly object _sync = new();
    private NamedPipeClientStream? _pipe;
    private CancellationTokenSource? _readerCts;
    private Task? _readerTask;

    public EzbfPipeClient(string pipeName) => _pipeName = pipeName;

    public string? LastConnectError { get; private set; }

    public bool IsConnected
    {
        get
        {
            lock (_sync)
            {
                return _pipe is { IsConnected: true };
            }
        }
    }

    public bool EnsureConnected(int retryCount = 5, int retryDelayMs = 500)
    {
        lock (_sync)
        {
            if (_pipe is { IsConnected: true })
            {
                return true;
            }

            return ConnectLocked(retryCount, retryDelayMs);
        }
    }

    public bool SendJson(string jsonPayload)
    {
        lock (_sync)
        {
            if (!EnsureConnected())
            {
                return false;
            }

            try
            {
                WriteFrame(jsonPayload);
                return true;
            }
            catch (Exception ex)
            {
                LastConnectError = ex.Message;
                DisconnectLocked();
                return false;
            }
        }
    }

    public void Disconnect()
    {
        lock (_sync)
        {
            StopReaderLocked();
            DisconnectLocked();
        }
    }

    public void Dispose()
    {
        Disconnect();
    }

    private bool ConnectLocked(int retryCount, int retryDelayMs)
    {
        StopReaderLocked();
        DisconnectLocked();

        for (int attempt = 0; attempt < retryCount; attempt++)
        {
            try
            {
                _pipe = new NamedPipeClientStream(
                    ".",
                    _pipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);

                _pipe.Connect(2000);
                if (!_pipe.IsConnected)
                {
                    LastConnectError = "Pipe connect returned without an active connection.";
                    DisconnectLocked();
                    WaitBeforeRetry(attempt, retryCount, retryDelayMs);
                    continue;
                }

                StartReaderLocked();
                LastConnectError = null;
                return true;
            }
            catch (TimeoutException)
            {
                LastConnectError =
                    $"Pipe '{_pipeName}' was not found. Start EezBotFun Configurator and enable Named Pipe Service.";
            }
            catch (UnauthorizedAccessException)
            {
                LastConnectError =
                    $"Pipe '{_pipeName}' is busy. Close other hardware monitor clients and try again.";
            }
            catch (IOException ex)
            {
                LastConnectError = ex.Message;
            }
            catch (Exception ex)
            {
                LastConnectError = ex.Message;
            }

            DisconnectLocked();
            WaitBeforeRetry(attempt, retryCount, retryDelayMs);
        }

        return false;
    }

    private void WriteFrame(string jsonPayload)
    {
        if (_pipe is not { IsConnected: true })
        {
            throw new IOException("Named pipe is not connected.");
        }

        byte[] payload = Encoding.UTF8.GetBytes(jsonPayload);
        if (payload.Length > 10 * 1024 * 1024)
        {
            throw new InvalidOperationException($"JSON payload too large ({payload.Length} bytes).");
        }

        byte[] header = new byte[12];
        Encoding.ASCII.GetBytes("EZBF", 0, 4, header, 0);
        header[4] = ProtocolVersion;
        header[5] = MessageTypeEvent;
        header[6] = 0;
        header[7] = 0;
        BitConverter.TryWriteBytes(header.AsSpan(8), (uint)payload.Length);

        _pipe.Write(header, 0, header.Length);
        _pipe.Write(payload, 0, payload.Length);
        _pipe.Flush();
    }

    private void StartReaderLocked()
    {
        if (_pipe == null)
        {
            return;
        }

        _readerCts = new CancellationTokenSource();
        NamedPipeClientStream pipe = _pipe;
        CancellationToken token = _readerCts.Token;

        _readerTask = Task.Run(async () =>
        {
            byte[] buffer = new byte[4096];
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!pipe.IsConnected)
                    {
                        break;
                    }

                    int count = await pipe.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false);
                    if (count <= 0)
                    {
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (IOException)
                {
                    break;
                }
            }
        }, token);
    }

    private void StopReaderLocked()
    {
        if (_readerCts == null)
        {
            return;
        }

        try
        {
            _readerCts.Cancel();
            _readerTask?.Wait(500);
        }
        catch
        {
            // ignored
        }

        _readerCts.Dispose();
        _readerCts = null;
        _readerTask = null;
    }

    private void DisconnectLocked()
    {
        if (_pipe == null)
        {
            return;
        }

        try
        {
            _pipe.Close();
        }
        catch
        {
            // ignored
        }

        _pipe.Dispose();
        _pipe = null;
    }

    private static void WaitBeforeRetry(int attempt, int retryCount, int retryDelayMs)
    {
        if (attempt < retryCount - 1)
        {
            Thread.Sleep(retryDelayMs);
        }
    }
}
