using System.Net;
using System.Security.Cryptography;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;

namespace Northstar.DataMigration;

/// <summary>
/// Read-only S3 access surface used by the migration utility. It intentionally
/// exposes only existence/size inspection and streamed SHA-256 computation; it
/// has no upload, copy, overwrite, delete, or metadata-modification operations.
/// </summary>
public interface IS3BlobReader
{
    Task<long?> TryGetSizeAsync(string bucketName, string key, CancellationToken cancellationToken = default);

    Task<string?> TryComputeSha256Async(string bucketName, string key, CancellationToken cancellationToken = default);
}

public sealed class S3BlobReader : IS3BlobReader
{
    private readonly IAmazonS3 _client;

    public S3BlobReader(IAmazonS3 client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public static S3BlobReader FromOptions(MigrationOptions options) =>
        new(string.IsNullOrWhiteSpace(options.S3Region)
            ? new AmazonS3Client()
            : new AmazonS3Client(RegionEndpoint.GetBySystemName(options.S3Region)));

    public async Task<long?> TryGetSizeAsync(
        string bucketName,
        string key,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.GetObjectMetadataAsync(
                new GetObjectMetadataRequest { BucketName = bucketName, Key = key },
                cancellationToken);
            return response.ContentLength;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<string?> TryComputeSha256Async(
        string bucketName,
        string key,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _client.GetObjectAsync(
                new GetObjectRequest { BucketName = bucketName, Key = key },
                cancellationToken);
            return Convert.ToHexString(
                await SHA256.HashDataAsync(response.ResponseStream, cancellationToken));
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}
