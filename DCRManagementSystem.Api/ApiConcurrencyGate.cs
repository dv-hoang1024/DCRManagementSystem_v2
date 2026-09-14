namespace DCRManagementSystem.Api;

public sealed class ApiConcurrencyGate
{
    private readonly SemaphoreSlim _semaphore;
    private readonly int _queueLimit;
    private readonly TimeSpan _queueTimeout;
    private int _waiting;

    public ApiConcurrencyGate(IConfiguration configuration)
    {
        var options = configuration.GetSection("ApiServer").Get<ApiServerOptions>() ?? new ApiServerOptions();
        var maxConcurrent = Math.Clamp(options.MaxConcurrentRequests, 4, 128);
        _queueLimit = Math.Clamp(options.RequestQueueLimit, maxConcurrent, 2000);
        _queueTimeout = TimeSpan.FromSeconds(Math.Clamp(options.RequestQueueTimeoutSeconds, 2, 120));
        _semaphore = new SemaphoreSlim(maxConcurrent, maxConcurrent);
    }

    public async Task<IDisposable?> TryEnterAsync(CancellationToken cancellationToken)
    {
        var waiting = Interlocked.Increment(ref _waiting);
        if (waiting > _queueLimit)
        {
            Interlocked.Decrement(ref _waiting);
            return null;
        }

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(_queueTimeout);
            try
            {
                await _semaphore.WaitAsync(linked.Token).ConfigureAwait(false);
                return new Lease(_semaphore);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
        }
        finally
        {
            Interlocked.Decrement(ref _waiting);
        }
    }

    private sealed class Lease : IDisposable
    {
        private SemaphoreSlim? _semaphore;
        public Lease(SemaphoreSlim semaphore) => _semaphore = semaphore;
        public void Dispose() => Interlocked.Exchange(ref _semaphore, null)?.Release();
    }
}
