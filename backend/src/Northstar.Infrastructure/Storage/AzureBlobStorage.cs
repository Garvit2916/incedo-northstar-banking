using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Northstar.Application.Abstractions;

namespace Northstar.Infrastructure.Storage;

/// <summary>
/// Azure Blob Storage implementation for private KYC documents. The container is
/// private; reviewers and owners receive short-lived, read-only SAS links.
/// </summary>
public sealed class AzureBlobStorage : IBlobStorage
{
    private readonly BlobContainerClient _container;

    public AzureBlobStorage(string connectionString, string containerName)
    {
        _container = new BlobContainerClient(connectionString, containerName);
    }

    public async Task<StoredBlob> SaveAsync(Stream content, string storageKey, string contentType, CancellationToken ct)
    {
        var blob = _container.GetBlobClient(storageKey);
        await blob.UploadAsync(content, new BlobHttpHeaders { ContentType = contentType }, cancellationToken: ct);

        var properties = await blob.GetPropertiesAsync(cancellationToken: ct);
        var hash = properties.Value.ContentHash is { Length: > 0 }
            ? Convert.ToHexString(properties.Value.ContentHash)
            : string.Empty;

        return new StoredBlob(storageKey, properties.Value.ContentLength, hash);
    }

    public async Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct)
    {
        var blob = _container.GetBlobClient(storageKey);
        var response = await blob.DownloadStreamingAsync(cancellationToken: ct);
        return response.Value.Content;
    }

    public async Task DeleteAsync(string storageKey, CancellationToken ct)
    {
        var blob = _container.GetBlobClient(storageKey);
        await blob.DeleteIfExistsAsync(cancellationToken: ct);
    }

    public Task<Uri?> GetPresignedUrlAsync(string storageKey, TimeSpan ttl, CancellationToken ct)
    {
        var blob = _container.GetBlobClient(storageKey);
        if (!blob.CanGenerateSasUri)
        {
            return Task.FromResult<Uri?>(null);
        }

        var sas = new BlobSasBuilder
        {
            BlobContainerName = _container.Name,
            BlobName = storageKey,
            Resource = "b",
            ExpiresOn = DateTimeOffset.UtcNow.Add(ttl)
        };
        sas.SetPermissions(BlobSasPermissions.Read);

        return Task.FromResult<Uri?>(blob.GenerateSasUri(sas));
    }
}
