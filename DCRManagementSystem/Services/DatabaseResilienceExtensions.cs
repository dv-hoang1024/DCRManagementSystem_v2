using System.Data;
using DCRManagementSystem.Data;
using Microsoft.EntityFrameworkCore;

namespace DCRManagementSystem.Services;

public static class DatabaseResilienceExtensions
{
    public static async Task OpenSqlConnectionWithRetryAsync(
        this AppDbContext db,
        CancellationToken cancellationToken = default)
    {
        await NetworkResilienceService.ExecuteSqlAsync(async token =>
        {
            var connection = db.Database.GetDbConnection();
            if (connection.State == ConnectionState.Broken)
                connection.Close();

            if (connection.State != ConnectionState.Open)
                await db.Database.OpenConnectionAsync(token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }
}
