using HardwareMonitor.Windows.Configuration;

namespace HardwareMonitor.Windows.Hosting;

/// <summary>
/// Runs <see cref="MonitorEngine"/> on a background thread until stopped.
/// </summary>
public sealed class MonitorLoopHost : IDisposable
{
    private readonly object _gate = new();
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

            PublishStatus(new RuntimeStatus
            {
                UpdatedAt = DateTimeOffset.Now,
                LastSummary = "Initializing hardware sensors…",
            });

            _cts = new CancellationTokenSource();
            _loopTask = Task.Run(() => RunLoopAsync(_cts.Token));
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

    private async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        using MonitorEngine engine = new();

        while (!stoppingToken.IsCancellationRequested)
        {
            MonitorSettings settings = MonitorSettingsStore.Load();

            try
            {
                MonitorTickResult tick = engine.RunOnce(settings, PublishStatus);
                PublishStatus(tick.Status);
            }
            catch (Exception ex)
            {
                PublishStatus(new RuntimeStatus
                {
                    UpdatedAt = DateTimeOffset.Now,
                    PipeConnected = false,
                    LastError = ex.Message,
                    LastSummary = "Sensor read failed",
                });
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
