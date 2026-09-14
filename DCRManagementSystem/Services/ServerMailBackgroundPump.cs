using DCRManagementSystem.Data;
using DCRManagementSystem.Helpers;

namespace DCRManagementSystem.Services;

/// <summary>
/// Lightweight foreground-application helper for the designated Mail Server.
/// It keeps EmailOutbox moving while DCRManagementSystem.exe is open, even when
/// the application is sitting on LoginForm. SQL sp_getapplock inside MailWorkerService
/// prevents overlap with Scheduled Task or another process instance.
/// </summary>
public sealed class ServerMailBackgroundPump : IDisposable
{
    private readonly Func<AppDbContext> _dbFactory;
    private readonly AppSettings _settings;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loopTask;
    private int _running;

    public ServerMailBackgroundPump(Func<AppDbContext> dbFactory, AppSettings settings)
    {
        _dbFactory = dbFactory;
        _settings = settings;
    }

    public void Start()
    {
        if (_loopTask is not null)
            return;

        _loopTask = Task.Run(() => RunLoopAsync(_cts.Token));
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        await TryRunOnceAsync(cancellationToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
                await TryRunOnceAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task TryRunOnceAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _running, 1) != 0)
            return;

        try
        {
            var state = await new MailWorkerStateService(_dbFactory).GetAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(state.MachineName) ||
                string.IsNullOrWhiteSpace(state.SignedInAccount) ||
                !string.Equals(state.MachineName, Environment.MachineName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(state.WindowsIdentity, MailWorkerStateService.GetCurrentWindowsIdentity(), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await new MailWorkerService(_dbFactory, _settings)
                .RunOnceAsync(50, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            // EmailOutbox is durable. The next loop/Scheduled Task retries and the
            // detailed worker state remains visible in System Settings.
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
