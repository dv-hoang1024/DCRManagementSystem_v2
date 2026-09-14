using System.Buffers;
using System.Security.Cryptography;

namespace DCRManagementSystem.Services;

public sealed record FileTransferResult(string Sha256, long Length);

/// <summary>
/// Performs one-pass file copies suitable for SMB/NAS storage.
/// The source is hashed while it is being copied, so the destination does not
/// have to be read back across the network just to calculate SHA-256.
/// A process-wide gate prevents multiple large uploads from the same client
/// from saturating the plant shared folder at the same time.
/// </summary>
public static class OptimizedFileTransferService
{
    private const int BufferSize = 4 * 1024 * 1024;
    private static readonly SemaphoreSlim UploadGate = new(1, 1);

    public static async Task<FileTransferResult> CopyWithSha256Async(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Không tìm thấy file nguồn để upload.", sourcePath);

        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(destinationDirectory))
            throw new InvalidOperationException("Không xác định được thư mục đích.");

        await UploadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(destinationDirectory);
            FileTransferResult? result = null;

            await NetworkResilienceService.ExecuteFileAsync(async retryToken =>
            {
                var partialPath = destinationPath + $".uploading-{Guid.NewGuid():N}";
                TryDelete(partialPath);

                try
                {
                    result = await CopySinglePassAsync(sourcePath, partialPath, retryToken).ConfigureAwait(false);

                    // Renaming a file inside the same SMB directory is a metadata operation.
                    // The final file therefore never appears half-written to other clients.
                    File.Move(partialPath, destinationPath, overwrite: false);
                }
                catch
                {
                    TryDelete(partialPath);
                    throw;
                }
            }, cancellationToken).ConfigureAwait(false);

            return result ?? throw new IOException("Upload không trả về kết quả truyền file.");
        }
        finally
        {
            UploadGate.Release();
        }
    }

    private static async Task<FileTransferResult> CopySinglePassAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;

            await using var source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            await using var target = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;

                hash.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                total += read;
            }

            await target.FlushAsync(cancellationToken).ConfigureAwait(false);
            return new FileTransferResult(
                Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(),
                total);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort cleanup. NetworkResilienceService will surface the real I/O error.
        }
    }
}
