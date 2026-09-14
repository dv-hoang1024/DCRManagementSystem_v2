using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using DCRManagementSystem.Data;
using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace DCRManagementSystem.Services;

public sealed class AttachmentService : IAttachmentService
{
    private readonly Func<AppDbContext> _dbFactory;
    private readonly AppSettings _settings;
    private readonly StorageConfigurationService _storage;

    public AttachmentService(Func<AppDbContext> dbFactory, AppSettings settings)
    {
        _dbFactory = dbFactory;
        _settings = settings;
        _storage = new StorageConfigurationService(dbFactory, settings);
    }

    public async Task UploadAsync(int requestId, string sourceFile, string attachmentType, int userId, bool isAdmin)
    {
        if (!File.Exists(sourceFile)) throw new FileNotFoundException("Không tìm thấy file nguồn.", sourceFile);
        attachmentType = AttachmentTypes.Normalize(attachmentType)
            ?? throw new InvalidOperationException("Attachment Type không hợp lệ.");

        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        var request = await db.DCRRequests.SingleOrDefaultAsync(x => x.Id == requestId)
            ?? throw new InvalidOperationException("Không tìm thấy DCR.");
        if (request.CreatedBy != userId && !isAdmin) throw new UnauthorizedAccessException("Bạn không có quyền upload file vào DCR này.");
        if (!RequestStatuses.IsEditable(request.Status)) throw new InvalidOperationException("Chỉ được thêm attachment khi DCR ở trạng thái Bản nháp hoặc Trả về.");

        var storageConfig = await _storage.GetAsync();
        var root = _storage.ResolveRoot(storageConfig);
        var requestFolder = Path.Combine(root, SanitizePathSegment(request.DCRNumber), $"R{request.RevisionNo}");
        var storageProvider = storageConfig.UseNetworkShare ? StorageProviders.NetworkShare : StorageProviders.FileSystem;
        var usedLocalFallback = false;
        try
        {
            await EnsureDirectoryAsync(requestFolder).ConfigureAwait(false);
        }
        catch (Exception ex) when (CanFallbackToLocal(storageConfig, ex))
        {
            (root, requestFolder) = ResolveLocalFallback(request);
            try
            {
                await EnsureDirectoryAsync(requestFolder).ConfigureAwait(false);
            }
            catch (Exception localEx)
            {
                throw CreateStorageException(root, localEx, ex);
            }
            storageProvider = StorageProviders.FileSystem;
            usedLocalFallback = true;
        }
        catch (Exception ex)
        {
            throw CreateStorageException(root, ex);
        }

        var sourceInfo = new FileInfo(sourceFile);
        var originalName = Path.GetFileName(sourceFile);
        var extension = sourceInfo.Extension;
        var compress = StorageConfigurationService.ShouldCompress(storageConfig, sourceInfo);
        var storedName = $"{Guid.NewGuid():N}{(compress ? ".zip" : extension)}";
        var destination = Path.Combine(requestFolder, storedName);

        string? localStagingZip = null;
        string originalHash;
        FileTransferResult transfer;

        try
        {
            if (compress)
            {
                // Compress locally first. This avoids using the SMB share as a working disk and
                // ensures the network sees only one sequential write of the final ZIP.
                var staged = await CreateLocalZipWithOriginalHashAsync(sourceFile, originalName).ConfigureAwait(false);
                localStagingZip = staged.ZipPath;
                originalHash = staged.OriginalSha256;
            }
            else
            {
                originalHash = string.Empty;
            }

            try
            {
                transfer = await TransferToAsync(destination).ConfigureAwait(false);
            }
            catch (Exception ex) when (!usedLocalFallback && CanFallbackToLocal(storageConfig, ex))
            {
                TryDeletePartialFile(destination);
                var networkException = ex;
                (root, requestFolder) = ResolveLocalFallback(request);
                destination = Path.Combine(requestFolder, storedName);
                try
                {
                    await EnsureDirectoryAsync(requestFolder).ConfigureAwait(false);
                    transfer = await TransferToAsync(destination).ConfigureAwait(false);
                }
                catch (Exception localEx)
                {
                    throw CreateStorageException(root, localEx, networkException);
                }
                storageProvider = StorageProviders.FileSystem;
                usedLocalFallback = true;
            }

            if (!compress) originalHash = transfer.Sha256;

            var attachment = new DCRAttachment
            {
                RequestId = requestId,
                AttachmentType = attachmentType,
                FileName = originalName,
                OriginalFileName = originalName,
                StoredFileName = storedName,
                FilePath = Path.GetFullPath(destination),
                FileExtension = extension,
                FileSize = sourceInfo.Length,
                StoredFileSize = transfer.Length,
                OriginalSha256Hash = originalHash,
                Sha256Hash = transfer.Sha256,
                StorageProvider = storageProvider,
                IsCompressed = compress,
                CompressionType = compress ? "ZIP" : string.Empty,
                UploadedBy = userId,
                UploadedAt = DateTime.Now,
                IsDeleted = false
            };

            db.DCRAttachments.Add(attachment);
            await db.SaveChangesAsync();
            AddAudit(db, requestId, userId, "Attachment Uploaded", attachment.Id.ToString(), string.Empty,
                $"{originalName}; original={sourceInfo.Length}; stored={transfer.Length}; compressed={compress}; SHA256={transfer.Sha256}; optimizedSinglePass=true; localFallback={usedLocalFallback}");
            await db.SaveChangesAsync();
        }
        catch
        {
            TryDeletePartialFile(destination);
            throw;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(localStagingZip))
                TryDeletePartialFile(localStagingZip);
        }

        async Task<FileTransferResult> TransferToAsync(string targetPath)
        {
            // One local read + one destination write. SHA-256 is calculated during the copy,
            // so neither the SMB destination nor the local fallback is read a second time.
            var input = compress ? localStagingZip! : sourceFile;
            return await OptimizedFileTransferService.CopyWithSha256Async(input, targetPath).ConfigureAwait(false);
        }
    }

    private static bool CanFallbackToLocal(FileStorageSettings settings, Exception exception) =>
        settings.UseNetworkShare &&
        settings.FallbackToLocalStorageWhenNetworkShareUnavailable &&
        exception.GetBaseException() is IOException or UnauthorizedAccessException or TimeoutException;

    private (string Root, string RequestFolder) ResolveLocalFallback(DCRRequest request)
    {
        // Keep fallback data outside the publish folder so upgrading/replacing the API
        // binaries cannot remove attachments that were accepted during an SMB outage.
        var root = Path.Combine(ApplicationDataPaths.PersistentRoot, "Data", "Attachments", "NetworkShareFallback");
        return (root, Path.Combine(root, SanitizePathSegment(request.DCRNumber), $"R{request.RevisionNo}"));
    }

    private static Task EnsureDirectoryAsync(string path) =>
        NetworkResilienceService.ExecuteFileAsync(cancellationToken =>
            Task.Run(() => Directory.CreateDirectory(path), cancellationToken));

    private static IOException CreateStorageException(string root, Exception exception, Exception? networkException = null)
    {
        var networkDetail = networkException is null
            ? string.Empty
            : $" Network Share ban đầu: {networkException.GetBaseException().Message}.";
        return new IOException(
            $"Không thể ghi attachment vào '{root}'. Kiểm tra quyền Read/Write/Modify của tài khoản Windows đang chạy DCR API và cấu hình Storage.{networkDetail} {exception.GetBaseException().Message}",
            exception);
    }

    public async Task DeleteAsync(int attachmentId, int userId, bool isAdmin)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        var attachment = await db.DCRAttachments.Include(x => x.Request)
            .SingleOrDefaultAsync(x => x.Id == attachmentId && !x.IsDeleted)
            ?? throw new InvalidOperationException("Không tìm thấy attachment.");
        var request = attachment.Request ?? throw new InvalidOperationException("DCR của attachment không tồn tại.");
        if (!RequestStatuses.IsEditable(request.Status)) throw new InvalidOperationException("Chỉ được xóa attachment khi DCR ở trạng thái Bản nháp hoặc Trả về.");
        if (request.CreatedBy != userId && !isAdmin) throw new UnauthorizedAccessException("Bạn không có quyền xóa attachment này.");
        attachment.IsDeleted = true;
        AddAudit(db, request.Id, userId, "Attachment Deleted", attachment.Id.ToString(), $"{attachment.FileName}; SHA256={attachment.Sha256Hash}", string.Empty);
        await db.SaveChangesAsync();
    }

    public async Task<string> GetAbsolutePathAsync(int attachmentId, int userId, bool isAdmin)
    {
        await using var db = _dbFactory();
        await db.OpenSqlConnectionWithRetryAsync();
        var attachment = await db.DCRAttachments.AsNoTracking().Include(x => x.Request)
            .SingleOrDefaultAsync(x => x.Id == attachmentId && !x.IsDeleted)
            ?? throw new InvalidOperationException("Không tìm thấy attachment.");
        var request = attachment.Request ?? throw new InvalidOperationException("DCR của attachment không tồn tại.");
        if (!isAdmin && request.CreatedBy != userId)
        {
            var isApprover = await db.DCRApprovalFlows.AsNoTracking().AnyAsync(x => x.RequestId == request.Id && x.ApproverId == userId);
            if (!isApprover) throw new UnauthorizedAccessException("Bạn không có quyền xem attachment này.");
        }

        var absolute = await ResolveAttachmentPathAsync(attachment);
        var actualHash = await NetworkResilienceService.ExecuteFileAsync(
            cancellationToken => ComputeSha256Async(absolute, cancellationToken));
        if (!string.IsNullOrWhiteSpace(attachment.Sha256Hash) && !actualHash.Equals(attachment.Sha256Hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("File attachment không vượt qua kiểm tra SHA-256; nội dung có thể đã bị thay đổi.");

        if (!attachment.IsCompressed) return absolute;
        return await NetworkResilienceService.ExecuteFileAsync(
            cancellationToken => ExtractCompressedFileToCacheAsync(attachment, absolute, cancellationToken));
    }

    public async Task OpenAsync(int attachmentId, int userId, bool isAdmin)
    {
        var path = await GetAbsolutePathAsync(attachmentId, userId, isAdmin);
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    private async Task<string> ResolveAttachmentPathAsync(DCRAttachment attachment)
    {
        if (Path.IsPathRooted(attachment.FilePath)) return Path.GetFullPath(attachment.FilePath);
        var config = await _storage.GetAsync();
        var root = attachment.StorageProvider == StorageProviders.FileSystem
            ? Path.GetFullPath(_settings.GetAbsoluteStorageRoot())
            : _storage.ResolveRoot(config);
        var combined = Path.GetFullPath(Path.Combine(root, attachment.FilePath));
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Đường dẫn attachment nằm ngoài storage root được cấu hình.");
        return combined;
    }

    private static async Task<(string ZipPath, string OriginalSha256)> CreateLocalZipWithOriginalHashAsync(
        string sourceFile,
        string originalName,
        CancellationToken cancellationToken = default)
    {
        var stagingRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DCRManagementSystem",
            "UploadStaging");
        Directory.CreateDirectory(stagingRoot);
        var zipPath = Path.Combine(stagingRoot, $"{Guid.NewGuid():N}.zip");

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(4 * 1024 * 1024);
        try
        {
            await using var target = new FileStream(
                zipPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4 * 1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            using (var archive = new ZipArchive(target, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = archive.CreateEntry(Path.GetFileName(originalName), CompressionLevel.Optimal);
                await using var entryStream = entry.Open();
                await using var source = new FileStream(
                    sourceFile,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    4 * 1024 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                while (true)
                {
                    var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                        break;
                    hash.AppendData(buffer, 0, read);
                    await entryStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }

            await target.FlushAsync(cancellationToken).ConfigureAwait(false);
            return (zipPath, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        }
        catch
        {
            TryDeletePartialFile(zipPath);
            throw;
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task<string> ExtractCompressedFileToCacheAsync(DCRAttachment attachment, string archivePath, CancellationToken cancellationToken)
    {
        var cache = Path.Combine(Path.GetTempPath(), "DCRManagementSystem", "AttachmentCache", attachment.Id.ToString());
        Directory.CreateDirectory(cache);
        var safeName = Path.GetFileName(attachment.OriginalFileName);
        var output = Path.Combine(cache, safeName);
        using var archive = ZipFile.OpenRead(archivePath);
        var entry = archive.Entries.FirstOrDefault(x => string.Equals(Path.GetFileName(x.FullName), safeName, StringComparison.OrdinalIgnoreCase))
            ?? archive.Entries.FirstOrDefault()
            ?? throw new InvalidDataException("ZIP attachment không có nội dung.");
        await using var source = entry.Open();
        await using var target = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous);
        await source.CopyToAsync(target, 1024 * 1024, cancellationToken).ConfigureAwait(false);
        target.Close();
        if (!string.IsNullOrWhiteSpace(attachment.OriginalSha256Hash))
        {
            var extractedHash = await ComputeSha256Async(output, cancellationToken);
            if (!extractedHash.Equals(attachment.OriginalSha256Hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Nội dung file giải nén không khớp SHA-256 ban đầu.");
        }
        return output;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)).ToLowerInvariant();
    }

    private static void TryDeletePartialFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort cleanup. The retry policy will surface the original I/O error.
        }
    }

    private static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat((value ?? string.Empty).Select(c => invalid.Contains(c) ? '_' : c));
    }

    private static void AddAudit(AppDbContext db, int requestId, int userId, string action, string entityId, string oldValue, string newValue)
    {
        db.AuditLogs.Add(new AuditLog
        {
            RequestId = requestId,
            UserId = userId,
            Action = action,
            EntityName = "DCRAttachment",
            EntityId = entityId,
            OldValue = oldValue,
            NewValue = newValue,
            ComputerName = AuditEnvironment.ComputerName,
            IpAddress = AuditEnvironment.LocalIpAddress,
            WindowsIdentity = AuditEnvironment.WindowsIdentityName,
            SessionId = RequestExecutionContext.SessionId,
            CreatedAt = DateTime.Now
        });
    }
}
