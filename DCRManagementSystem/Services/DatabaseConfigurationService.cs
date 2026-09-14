using DCRManagementSystem.Helpers;
using Microsoft.Data.SqlClient;

namespace DCRManagementSystem.Services;

public sealed class DatabaseConfigurationService
{
    private readonly AppSettings _appSettings;

    public DatabaseConfigurationService(AppSettings appSettings)
    {
        _appSettings = appSettings;
    }

    public DatabaseConnectionSettings GetCurrent()
    {
        if (DatabaseConfigurationStore.TryLoad(out var local) && local is not null)
            return local.Clone();
        return _appSettings.Database.Clone();
    }

    public async Task<DatabaseConnectionTestResult> TestConnectionAsync(
        DatabaseConnectionSettings settings,
        CancellationToken cancellationToken = default)
    {
        settings.Validate();

        return await NetworkResilienceService.ExecuteSqlAsync(async token =>
        {
            await using var connection = new SqlConnection(settings.BuildConnectionString());
            await connection.OpenAsync(token).ConfigureAwait(false);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    CONVERT(nvarchar(256), SERVERPROPERTY('ServerName')),
                    DB_NAME(),
                    CONVERT(nvarchar(128), SUSER_SNAME()),
                    CONVERT(nvarchar(128), SERVERPROPERTY('ProductVersion'));
                """;
            command.CommandTimeout = Math.Clamp(settings.ConnectTimeoutSeconds, 3, 120);

            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (!await reader.ReadAsync(token).ConfigureAwait(false))
                throw new InvalidOperationException("SQL Server không trả về thông tin kết nối.");

            return new DatabaseConnectionTestResult(
                reader.IsDBNull(0) ? settings.ServerAddress : reader.GetString(0),
                reader.IsDBNull(1) ? settings.DatabaseName : reader.GetString(1),
                reader.IsDBNull(2) ? settings.Username : reader.GetString(2),
                reader.IsDBNull(3) ? string.Empty : reader.GetString(3));
        }, cancellationToken).ConfigureAwait(false);
    }

    public void SaveForNextStartup(DatabaseConnectionSettings settings)
    {
        settings.Validate();
        DatabaseConfigurationStore.Save(settings);
    }

    public string GetMaskedConnectionSummary(DatabaseConnectionSettings settings)
    {
        var auth = settings.AuthenticationMode.Equals(DatabaseAuthenticationModes.Windows, StringComparison.OrdinalIgnoreCase)
            ? "Windows Authentication"
            : $"SQL Login: {settings.Username}";
        return $"{settings.ServerAddress}:{settings.Port} / {settings.DatabaseName} / {auth}";
    }
}

public sealed record DatabaseConnectionTestResult(
    string ServerName,
    string DatabaseName,
    string LoginName,
    string ProductVersion);
