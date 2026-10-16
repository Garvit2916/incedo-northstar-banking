using System.Security.Cryptography;
using Northstar.Application.Abstractions;

namespace Northstar.Infrastructure.Storage;

/// <summary>
/// Development blob store writing to a private directory outside the web root.
/// Storage keys are server-generated, and every resolved path is verified to stay
/// inside the configured root to prevent traversal.
/// </summary>
public sealed class FileSystemBlobStorage : IBlobStorage
{
    private readonly string _root;

    public FileSystemBlobStorage(string rootPath)
    {
        _root = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(_root);
    }

    public async Task<StoredBlob> SaveAsync(Stream content, string storageKey, string contentType, CancellationToken ct)
    {
        var path = ResolvePath(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await content.CopyToAsync(file, ct);
        }

        var info = new FileInfo(path);
        await using var read = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(read, ct));

        return new StoredBlob(storageKey, info.Length, hash);
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct)
    {
        var path = ResolvePath(storageKey);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Stored document was not found.", storageKey);
        }

        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct)
    {
        var path = ResolvePath(storageKey);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task<Uri?> GetPresignedUrlAsync(string storageKey, TimeSpan ttl, CancellationToken ct) =>
        Task.FromResult<Uri?>(null);

    private string ResolvePath(string storageKey)
    {
        var combined = Path.GetFullPath(Path.Combine(_root, storageKey));
        if (!combined.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Resolved storage path escaped the storage root.");
        }

        return combined;
    }
}
