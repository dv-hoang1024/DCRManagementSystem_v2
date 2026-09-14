using System.Collections.Concurrent;
using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;
using DCRManagementSystem.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DCRManagementSystem.Api;

public sealed class ApiUserSessionCache
{
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _lifetime;
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();

    public ApiUserSessionCache(IMemoryCache cache, IConfiguration configuration)
    {
        _cache = cache;
        var options = configuration.GetSection("ApiServer").Get<ApiServerOptions>() ?? new ApiServerOptions();
        _lifetime = TimeSpan.FromSeconds(Math.Clamp(options.UserSessionCacheSeconds, 5, 300));
    }

    public async Task<User?> GetActiveUserAsync(int userId, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue<User>(CacheKey(userId), out var cached)) return cached;

        var sync = _locks.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await sync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_cache.TryGetValue<User>(CacheKey(userId), out cached)) return cached;

            await using var db = AppServices.CreateDbContext();
            await db.OpenSqlConnectionWithRetryAsync(cancellationToken).ConfigureAwait(false);
            var user = await db.Users.AsNoTracking()
                .Include(x => x.Department)
                .Include(x => x.BusinessUnit)
                .SingleOrDefaultAsync(x => x.UserId == userId && x.IsActive && !x.IsDeleted, cancellationToken)
                .ConfigureAwait(false);

            if (user is not null)
                _cache.Set(CacheKey(userId), user, _lifetime);
            return user;
        }
        finally
        {
            sync.Release();
        }
    }

    public void Invalidate(int userId) => _cache.Remove(CacheKey(userId));
    private static string CacheKey(int userId) => $"api-user:{userId}";
}
