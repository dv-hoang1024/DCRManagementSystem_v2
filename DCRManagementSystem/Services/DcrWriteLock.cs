using DCRManagementSystem.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DCRManagementSystem.Services;

internal static class DcrWriteLock
{
    // Database-owned locks also serialize desktop clients connected directly to SQL.
    public static async Task AcquireAsync(AppDbContext db, string resource)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("DCR write lock requires a transaction.");
        command.CommandText = "DECLARE @r int; EXEC @r=sp_getapplock @Resource=@resource, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=15000; SELECT @r;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@resource";
        parameter.Value = resource;
        command.Parameters.Add(parameter);
        if (Convert.ToInt32(await command.ExecuteScalarAsync()) < 0)
            throw new InvalidOperationException("DCR đang được xử lý. Vui lòng thử lại sau.");
    }
}
