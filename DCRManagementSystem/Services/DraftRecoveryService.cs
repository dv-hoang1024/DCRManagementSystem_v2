using System.Text.Json;
using DCRManagementSystem.Models;

namespace DCRManagementSystem.Services;

public sealed class DraftRecoveryService
{
    private readonly string _root;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private sealed class RecoveryEnvelope
    {
        public DateTime SavedAt { get; set; }
        public int UserId { get; set; }
        public int? RequestId { get; set; }
        public DcrEditModel Model { get; set; } = new();
    }

    public sealed class RecoveryResult
    {
        public DateTime SavedAt { get; init; }
        public DcrEditModel Model { get; init; } = new();
    }

    public DraftRecoveryService()
    {
        _root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DCRManagementSystem",
            "DraftRecovery");
        Directory.CreateDirectory(_root);
    }

    public void Save(int userId, int? requestId, DcrEditModel model)
    {
        var path = GetPath(userId, requestId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var envelope = new RecoveryEnvelope
        {
            SavedAt = DateTime.Now,
            UserId = userId,
            RequestId = requestId,
            Model = model
        };

        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(envelope, JsonOptions));
        File.Move(temp, path, true);
    }

    public RecoveryResult? TryLoad(int userId, int? requestId)
    {
        var path = GetPath(userId, requestId);
        if (!File.Exists(path))
            return null;

        try
        {
            var envelope = JsonSerializer.Deserialize<RecoveryEnvelope>(File.ReadAllText(path));
            if (envelope is null || envelope.UserId != userId || envelope.RequestId != requestId)
                return null;

            return new RecoveryResult
            {
                SavedAt = envelope.SavedAt,
                Model = envelope.Model
            };
        }
        catch
        {
            return null;
        }
    }

    public void Delete(int userId, int? requestId)
    {
        var path = GetPath(userId, requestId);
        if (File.Exists(path))
            File.Delete(path);
    }

    public void MoveNewDraftToRequest(int userId, int requestId)
    {
        var source = GetPath(userId, null);
        var destination = GetPath(userId, requestId);
        if (!File.Exists(source))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(source, destination, true);
    }

    private string GetPath(int userId, int? requestId)
    {
        var userFolder = Path.Combine(_root, $"User_{userId}");
        var fileName = requestId.HasValue ? $"Request_{requestId.Value}.json" : "New_DCR.json";
        return Path.Combine(userFolder, fileName);
    }
}
