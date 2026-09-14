using DCRManagementSystem.Data;
using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace DCRManagementSystem.Services;

public sealed class WebPortalConfigurationService : IWebPortalConfigurationService
{
    private const string Prefix = "WebPortal.";
    private const string DefaultBaseUrl = "https://dcr.ggpcontrol.cloud";
    private readonly Func<AppDbContext> _dbFactory;

    public WebPortalConfigurationService(Func<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<WebPortalSettings> GetAsync()
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        return await GetAsync(db);
    }

    public async Task<WebPortalSettings> GetAsync(AppDbContext db)
    {
        var rows = await db.SystemSettings.AsNoTracking()
            .Where(x => x.Key.StartsWith(Prefix))
            .ToDictionaryAsync(x => x.Key, x => x.Value);

        static bool ReadBool(Dictionary<string, string> rows, string key, bool fallback)
            => rows.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : fallback;

        return new WebPortalSettings
        {
            Enabled = ReadBool(rows, Prefix + "Enabled", false),
            AllowCreate = ReadBool(rows, Prefix + "AllowCreate", true),
            AllowApproval = ReadBool(rows, Prefix + "AllowApproval", true),
            BaseUrl = rows.TryGetValue(Prefix + "BaseUrl", out var url) && !string.IsNullOrWhiteSpace(url)
                ? NormalizeLegacyBaseUrl(url)
                : DefaultBaseUrl
        };
    }

    private static string NormalizeLegacyBaseUrl(string value)
    {
        var url = value.Trim().TrimEnd('/');
        return url.Equals("https://ggpcontrol.cloud", StringComparison.OrdinalIgnoreCase) ||
               url.Equals("https://ggpcontrol.cloud/dcr", StringComparison.OrdinalIgnoreCase)
            ? DefaultBaseUrl
            : url;
    }

    public async Task SaveAsync(WebPortalSettings value)
    {
        value ??= new WebPortalSettings();
        value.Validate();

        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Prefix + "Enabled"] = value.Enabled.ToString(),
            [Prefix + "AllowCreate"] = value.AllowCreate.ToString(),
            [Prefix + "AllowApproval"] = value.AllowApproval.ToString(),
            [Prefix + "BaseUrl"] = value.BaseUrl
        };

        foreach (var pair in values)
        {
            var row = await db.SystemSettings.SingleOrDefaultAsync(x => x.Key == pair.Key);
            if (row is null)
            {
                db.SystemSettings.Add(new SystemSetting
                {
                    Key = pair.Key,
                    Value = pair.Value,
                    UpdatedAt = DateTime.Now
                });
            }
            else
            {
                row.Value = pair.Value;
                row.UpdatedAt = DateTime.Now;
            }
        }

        await db.SaveChangesAsync();
    }
}
