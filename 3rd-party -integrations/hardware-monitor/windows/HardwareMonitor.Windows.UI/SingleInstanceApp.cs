using System.IO.Pipes;

namespace HardwareMonitor.Windows.UI;

internal sealed class SingleInstanceApp : IDisposable
{
    private const string PipeName = "EezBotFun.HardwareMonitor.Activate";

    private readonly Mutex? _mutex;
    private CancellationTokenSource? _activationServerCts;

    private SingleInstanceApp(Mutex? mutex)
    {
        _mutex = mutex;
        IsFirst = mutex != null;
    }

    public bool IsFirst { get; }

    public static SingleInstanceApp Acquire()
    {
        const string mutexName = @"Global\EezBotFun.HardwareMonitor";

        Mutex mutex = new(initiallyOwned: true, mutexName, out bool createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return new SingleInstanceApp(null);
        }

        return new SingleInstanceApp(mutex);
    }

    public static bool TryNotifyExistingInstance()
    {
        try
        {
            using NamedPipeClientStream client = new(".", PipeName, PipeDirection.Out);
            client.Connect(300);
            client.WriteByte(1);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void StartActivationServer(Action onActivate)
    {
        if (!IsFirst)
        {
            return;
        }

        _activationServerCts = new CancellationTokenSource();
        CancellationToken token = _activationServerCts.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using NamedPipeServerStream server = new(
                        PipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                    onActivate();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    try
                    {
                        await Task.Delay(200, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }, token);
    }

    public void Dispose()
    {
        _activationServerCts?.Cancel();
        _activationServerCts?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
    }
}
