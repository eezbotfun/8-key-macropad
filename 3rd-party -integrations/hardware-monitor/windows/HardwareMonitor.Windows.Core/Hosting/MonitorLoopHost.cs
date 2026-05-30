using HardwareMonitor.Windows.Configuration;

namespace HardwareMonitor.Windows.Hosting;

/// <summary>
/// Runs <see cref="MonitorEngine"/> on a background thread until stopped.
/// </summary>
public sealed class MonitorLoopHost : IDisposable
{
    private readonly object _gate = new();
    private MonitorEngine? _engine;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private RuntimeStatus? _latestStatus;

    public event EventHandler<RuntimeStatus>? StatusUpdated;

    public RuntimeStatus? LatestStatus
    {
        get
        {
            lock (_gate)
            {
                return _latestStatus;
            }
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _loopTask is { IsCompleted: false };
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_loopTask is { IsCompleted: false })
            {
                return;
            }

            _engine ??= new MonitorEngine();
            _cts = new CancellationTokenSource();
            MonitorEngine engine = _engine;
            _loopTask = Task.Run(() => RunLoopAsync(engine, _cts.Token));
        }
    }

    public async Task StopAsync()
    {
        Task? loopTask;
        CancellationTokenSource? cts;

        lock (_gate)
        {
            loopTask = _loopTask;
            cts = _cts;
            _loopTask = null;
            _cts = null;
        }

        if (cts == null)
        {
            return;
        }

        await cts.CancelAsync().ConfigureAwait(false);

        if (loopTask != null)
        {
            try
            {
                await loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        cts.Dispose();
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        lock (_gate)
        {
            _engine?.Dispose();
            _engine = null;
        }
    }

    private void PublishStatus(RuntimeStatus status)
    {
        EventHandler<RuntimeStatus>? handler;
        lock (_gate)
        {
            _latestStatus = status;
            handler = StatusUpdated;
        }

        handler?.Invoke(this, status);
    }

    private async Task RunLoopAsync(MonitorEngine engine, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            MonitorSettings settings = MonitorSettingsStore.Load();

            try
            {
                MonitorTickResult tick = engine.RunOnce(settings);
                PublishStatus(tick.Status);
            }
            catch
            {
                // Keep looping; next tick may succeed when configurator connects.
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(Math.Max(0.5, settings.IntervalSeconds)),
                    stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
