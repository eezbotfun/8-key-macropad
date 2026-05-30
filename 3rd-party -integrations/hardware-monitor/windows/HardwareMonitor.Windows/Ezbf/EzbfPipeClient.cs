using System.IO.Pipes;
using System.Text;

namespace HardwareMonitor.Windows.Ezbf;

internal sealed class EzbfPipeClient : IDisposable
{
    private const byte ProtocolVersion = 1;
    private const byte MessageTypeEvent = 0x20;

    private readonly string _pipeName;
    private NamedPipeClientStream? _pipe;

    public EzbfPipeClient(string pipeName) => _pipeName = pipeName;

    public bool IsConnected => _pipe?.IsConnected == true;

    public bool Connect(int retryCount = 5, int retryDelayMs = 500)
    {
        for (int attempt = 0; attempt < retryCount; attempt++)
        {
            try
            {
                Disconnect();
                _pipe = new NamedPipeClientStream(
                    ".",
                    _pipeName,
                    PipeDirection.InOut,
                    PipeOptions.None);

                _pipe.Connect(retryDelayMs * 2);
                return true;
            }
            catch (TimeoutException)
            {
                if (attempt == retryCount - 1)
                {
                    return false;
                }

                Thread.Sleep(retryDelayMs);
            }
            catch (IOException)
            {
                if (attempt == retryCount - 1)
                {
                    return false;
                }

                Thread.Sleep(retryDelayMs);
            }
        }

        return false;
    }

    public bool SendJson(string jsonPayload)
    {
        if (_pipe is not { IsConnected: true })
        {
            return false;
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
        return true;
    }

    public void Disconnect()
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

    public void Dispose() => Disconnect();
}
