using DCRManagementSystem.Data;
using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace DCRManagementSystem.Services;

public sealed class StorageConfigurationService : IStorageConfigurationService
{
    private const string Prefix = "Storage.";
    private readonly Func<AppDbContext> _dbFactory;
    private readonly AppSettings _settings;

    public StorageConfigurationService(Func<AppDbContext> dbFactory, AppSettings settings)
    {
        _dbFactory = dbFactory;
        _settings = settings;
    }

    public async Task<FileStorageSettings> GetAsync()
    {
        var result = Clone(_settings.Storage);
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        var rows = await db.SystemSettings.AsNoTracking()
            .Where(x => x.Key.StartsWith(Prefix))
            .ToDictionaryAsync(x => x.Key, x => x.Value);

        if (rows.TryGetValue(Prefix + nameof(FileStorageSettings.UseNetworkShare), out var useShare) && bool.TryParse(useShare, out var b1)) result.UseNetworkShare = b1;
        if (rows.TryGetValue(Prefix + nameof(FileStorageSettings.FallbackToLocalStorageWhenNetworkShareUnavailable), out var fallback) && bool.TryParse(fallback, out var b3)) result.FallbackToLocalStorageWhenNetworkShareUnavailable = b3;
        if (rows.TryGetValue(Prefix + nameof(FileStorageSettings.ServerAddress), out var server)) result.ServerAddress = server;
        if (rows.TryGetValue(Prefix + nameof(FileStorageSettings.ShareName), out var share)) result.ShareName = share;
        if (rows.TryGetValue(Prefix + nameof(FileStorageSettings.RootSubfolder), out var sub)) result.RootSubfolder = sub;
        if (rows.TryGetValue(Prefix + nameof(FileStorageSettings.CompressLargeTechnicalFiles), out var compress) && bool.TryParse(compress, out var b2)) result.CompressLargeTechnicalFiles = b2;
        if (rows.TryGetValue(Prefix + nameof(FileStorageSettings.CompressionThresholdMb), out var threshold) && int.TryParse(threshold, out var i)) result.CompressionThresholdMb = Math.Clamp(i, 1, 102400);
        if (rows.TryGetValue(Prefix + nameof(FileStorageSettings.CompressionExtensions), out var ext)) result.CompressionExtensions = ext;
        return result;
    }

    public async Task SaveAsync(FileStorageSettings value)
    {
        Validate(value);
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        var values = new Dictionary<string, string>
        {
            [Prefix + nameof(FileStorageSettings.UseNetworkShare)] = value.UseNetworkShare.ToString(),
            [Prefix + nameof(FileStorageSettings.FallbackToLocalStorageWhenNetworkShareUnavailable)] = value.FallbackToLocalStorageWhenNetworkShareUnavailable.ToString(),
            [Prefix + nameof(FileStorageSettings.ServerAddress)] = value.ServerAddress.Trim(),
            [Prefix + nameof(FileStorageSettings.ShareName)] = value.ShareName.Trim(),
            [Prefix + nameof(FileStorageSettings.RootSubfolder)] = value.RootSubfolder.Trim(),
            [Prefix + nameof(FileStorageSettings.CompressLargeTechnicalFiles)] = value.CompressLargeTechnicalFiles.ToString(),
            [Prefix + nameof(FileStorageSettings.CompressionThresholdMb)] = value.CompressionThresholdMb.ToString(),
            [Prefix + nameof(FileStorageSettings.CompressionExtensions)] = value.CompressionExtensions.Trim()
        };

        foreach (var pair in values)
        {
            var row = await db.SystemSettings.SingleOrDefaultAsync(x => x.Key == pair.Key);
            if (row is null)
            {
                db.SystemSettings.Add(new SystemSetting { Key = pair.Key, Value = pair.Value, UpdatedAt = DateTime.Now });
            }
            else
            {
                row.Value = pair.Value;
                row.UpdatedAt = DateTime.Now;
            }
        }
        await db.SaveChangesAsync();
    }

    public async Task<string> TestConnectionAsync(FileStorageSettings value)
    {
        Validate(value);
        var root = ResolveRoot(value);
        await NetworkResilienceService.ExecuteFileAsync(async cancellationToken =>
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(root);
                var testFile = Path.Combine(root, $".dcr-write-test-{Environment.MachineName}-{Guid.NewGuid():N}.tmp");
                try
                {
                    File.WriteAllText(testFile, DateTime.Now.ToString("O"));
                    using var stream = new FileStream(testFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (stream.Length == 0)
                        throw new IOException("File Server tạo được file nhưng không ghi được dữ liệu.");
                }
                finally
                {
                    try { if (File.Exists(testFile)) File.Delete(testFile); } catch { }
                }
            }, cancellationToken).ConfigureAwait(false);
        });
        return root;
    }

    public string ResolveRoot(FileStorageSettings value)
    {
        if (!value.UseNetworkShare)
            return Path.GetFullPath(_settings.GetAbsoluteStorageRoot());

        var server = value.ServerAddress.Trim().Trim('\\', '/');
        var share = value.ShareName.Trim().Trim('\\', '/');
        var root = $@"\\{server}\{share}";
        var sub = value.RootSubfolder.Trim().Trim('\\', '/');
        return string.IsNullOrWhiteSpace(sub) ? root : Path.Combine(root, sub);
    }

    public static bool ShouldCompress(FileStorageSettings settings, FileInfo info)
    {
        if (!settings.CompressLargeTechnicalFiles) return false;
        if (info.Length < (long)Math.Max(1, settings.CompressionThresholdMb) * 1024L * 1024L) return false;
        var extensions = settings.CompressionExtensions
            .Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.StartsWith('.') ? x : "." + x)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return extensions.Contains(info.Extension);
    }

    private static void Validate(FileStorageSettings value)
    {
        if (value.CompressionThresholdMb < 1) throw new InvalidOperationException("Compression Threshold phải từ 1 MB trở lên.");
        if (!value.UseNetworkShare) return;
        if (string.IsNullOrWhiteSpace(value.ServerAddress)) throw new InvalidOperationException("Server Address không được để trống.");
        if (string.IsNullOrWhiteSpace(value.ShareName)) throw new InvalidOperationException("Folder Share không được để trống.");
        if (value.ServerAddress.IndexOfAny(Path.GetInvalidPathChars()) >= 0) throw new InvalidOperationException("Server Address chứa ký tự không hợp lệ.");
        if (value.ShareName.Contains("..", StringComparison.Ordinal) || value.ShareName.IndexOfAny(['\\', '/', ':']) >= 0)
            throw new InvalidOperationException("Folder Share chỉ được nhập tên share, ví dụ AutoUpdate.");
        var subfolder = value.RootSubfolder.Trim();
        if (Path.IsPathRooted(subfolder) || subfolder.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).Any(x => x == ".."))
            throw new InvalidOperationException("Subfolder không được là đường dẫn tuyệt đối hoặc chứa '..'.");
    }

    private static FileStorageSettings Clone(FileStorageSettings value) => new()
    {
        UseNetworkShare = value.UseNetworkShare,
        FallbackToLocalStorageWhenNetworkShareUnavailable = value.FallbackToLocalStorageWhenNetworkShareUnavailable,
        ServerAddress = value.ServerAddress,
        ShareName = value.ShareName,
        RootSubfolder = value.RootSubfolder,
        CompressLargeTechnicalFiles = value.CompressLargeTechnicalFiles,
        CompressionThresholdMb = value.CompressionThresholdMb,
        CompressionExtensions = value.CompressionExtensions
    };
}
