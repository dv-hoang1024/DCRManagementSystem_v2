using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace DCRManagementSystem.Helpers;

public static class DatabaseAuthenticationModes
{
    public const string SqlServer = "SqlServer";
    public const string Windows = "Windows";
}

public sealed class DatabaseConnectionSettings
{
    public string ServerAddress { get; set; } = "localhost";
    public int Port { get; set; } = 1433;
    public string DatabaseName { get; set; } = "DCRManagement";
    public string AuthenticationMode { get; set; } = DatabaseAuthenticationModes.SqlServer;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool Encrypt { get; set; } = true;
    public bool TrustServerCertificate { get; set; } = true;
    public int ConnectTimeoutSeconds { get; set; } = 10;
    public int CommandTimeoutSeconds { get; set; } = 30;
    public int MinPoolSize { get; set; } = 4;
    public int MaxPoolSize { get; set; } = 64;

    public string BuildConnectionString()
    {
        Validate();

        var builder = new SqlConnectionStringBuilder
        {
            DataSource = Port > 0 ? $"{ServerAddress.Trim()},{Port}" : ServerAddress.Trim(),
            InitialCatalog = DatabaseName.Trim(),
            IntegratedSecurity = AuthenticationMode.Equals(DatabaseAuthenticationModes.Windows, StringComparison.OrdinalIgnoreCase),
            MultipleActiveResultSets = true,
            ConnectTimeout = Math.Clamp(ConnectTimeoutSeconds, 3, 120),
            ConnectRetryCount = 3,
            ConnectRetryInterval = 1,
            ApplicationName = "DCR Management System",
            Pooling = true,
            MinPoolSize = Math.Clamp(MinPoolSize, 0, 32),
            MaxPoolSize = Math.Clamp(MaxPoolSize, 16, 200)
        };

        builder["Encrypt"] = Encrypt;
        builder["TrustServerCertificate"] = TrustServerCertificate;

        if (!builder.IntegratedSecurity)
        {
            builder.UserID = Username.Trim();
            builder.Password = Password;
            builder.PersistSecurityInfo = false;
        }

        return builder.ConnectionString;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ServerAddress))
            throw new InvalidOperationException("SQL Server Address không được để trống.");
        if (Port is < 0 or > 65535)
            throw new InvalidOperationException("SQL Server Port phải nằm trong khoảng 0-65535.");
        if (string.IsNullOrWhiteSpace(DatabaseName))
            throw new InvalidOperationException("Database Name không được để trống.");

        var windows = AuthenticationMode.Equals(DatabaseAuthenticationModes.Windows, StringComparison.OrdinalIgnoreCase);
        var sql = AuthenticationMode.Equals(DatabaseAuthenticationModes.SqlServer, StringComparison.OrdinalIgnoreCase);
        if (!windows && !sql)
            throw new InvalidOperationException("Authentication Mode của SQL Server không hợp lệ.");

        if (sql && string.IsNullOrWhiteSpace(Username))
            throw new InvalidOperationException("SQL Username không được để trống.");
        if (sql && string.IsNullOrEmpty(Password))
            throw new InvalidOperationException("SQL Password không được để trống.");
        if (MinPoolSize < 0 || MaxPoolSize < 1 || MinPoolSize > MaxPoolSize)
            throw new InvalidOperationException("SQL connection pool không hợp lệ: MinPoolSize phải >= 0 và <= MaxPoolSize.");
    }

    public string ApplyPerformanceOptions(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return connectionString;
        var builder = new SqlConnectionStringBuilder(connectionString)
        {
            Pooling = true,
            MinPoolSize = Math.Clamp(MinPoolSize, 0, 32),
            MaxPoolSize = Math.Clamp(MaxPoolSize, 16, 200),
            ConnectTimeout = Math.Clamp(ConnectTimeoutSeconds, 3, 120),
            ConnectRetryCount = 3,
            ConnectRetryInterval = 1,
            ApplicationName = "DCR Management System API"
        };
        return builder.ConnectionString;
    }

    public DatabaseConnectionSettings Clone() => new()
    {
        ServerAddress = ServerAddress,
        Port = Port,
        DatabaseName = DatabaseName,
        AuthenticationMode = AuthenticationMode,
        Username = Username,
        Password = Password,
        Encrypt = Encrypt,
        TrustServerCertificate = TrustServerCertificate,
        ConnectTimeoutSeconds = ConnectTimeoutSeconds,
        CommandTimeoutSeconds = CommandTimeoutSeconds,
        MinPoolSize = MinPoolSize,
        MaxPoolSize = MaxPoolSize
    };
}

public static class DatabaseConfigurationStore
{
    public static string ConfigurationPath => ApplicationDataPaths.DatabaseConfigPath;

    public static bool TryLoad(out DatabaseConnectionSettings? settings)
    {
        settings = null;

        if (!File.Exists(ConfigurationPath))
            TryMigrateConfiguration();

        if (!File.Exists(ConfigurationPath)) return false;

        try
        {
            var json = File.ReadAllText(ConfigurationPath);
            settings = JsonSerializer.Deserialize<DatabaseConnectionSettings>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            settings?.Validate();
            return settings is not null;
        }
        catch
        {
            settings = null;
            return false;
        }
    }

    private static void TryMigrateConfiguration()
    {
        var candidates = new[]
        {
            ApplicationDataPaths.ProvisionedDatabaseConfigPath,
            Path.Combine(AppContext.BaseDirectory, "Data", "Config", "database.config.json")
        };

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                var json = File.ReadAllText(candidate);
                var parsed = JsonSerializer.Deserialize<DatabaseConnectionSettings>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                parsed?.Validate();
                if (parsed is null) continue;

                Directory.CreateDirectory(Path.GetDirectoryName(ConfigurationPath)!);
                File.WriteAllText(ConfigurationPath, json);
                return;
            }
            catch
            {
                // Keep trying the next migration source.
            }
        }
    }

    public static void ApplyLocalOverride(AppSettings appSettings)
    {
        if (!TryLoad(out var local) || local is null) return;
        appSettings.Database = local;
        appSettings.ConnectionString = local.BuildConnectionString();
    }

    public static void Save(DatabaseConnectionSettings settings)
    {
        settings.Validate();
        var directory = Path.GetDirectoryName(ConfigurationPath)
            ?? throw new InvalidOperationException("Không xác định được thư mục lưu cấu hình SQL Server.");
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        var temp = ConfigurationPath + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, ConfigurationPath, true);
    }
}
