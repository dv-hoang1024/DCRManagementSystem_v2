using Microsoft.Data.SqlClient;
using Polly;
using Polly.Retry;

namespace DCRManagementSystem.Services;

public static class NetworkResilienceService
{
    private static readonly ResiliencePipeline FileIoPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder()
                .Handle<IOException>()
                .Handle<TimeoutException>(),
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromMilliseconds(1500),
            BackoffType = DelayBackoffType.Constant,
            UseJitter = true
        })
        .Build();

    private static readonly ResiliencePipeline SqlPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder()
                .Handle<SqlException>()
                .Handle<TimeoutException>(),
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromMilliseconds(1500),
            BackoffType = DelayBackoffType.Constant,
            UseJitter = true
        })
        .Build();

    public static async Task ExecuteFileAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        await FileIoPipeline.ExecuteAsync(
            async token => await action(token).ConfigureAwait(false),
            cancellationToken);
    }

    public static async Task<T> ExecuteFileAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        return await FileIoPipeline.ExecuteAsync(
            async token => await action(token).ConfigureAwait(false),
            cancellationToken);
    }

    public static void ExecuteSql(Action action)
    {
        SqlPipeline.Execute(action);
    }


    public static async Task ExecuteSqlAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        await SqlPipeline.ExecuteAsync(
            async token => await action(token).ConfigureAwait(false),
            cancellationToken);
    }

    public static async Task<T> ExecuteSqlAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        return await SqlPipeline.ExecuteAsync(
            async token => await action(token).ConfigureAwait(false),
            cancellationToken);
    }
}
