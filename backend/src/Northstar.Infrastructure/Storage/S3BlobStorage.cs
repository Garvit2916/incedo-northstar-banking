using System.Security.Cryptography;
using Amazon.S3;
using Amazon.S3.Model;
using Northstar.Application.Abstractions;

namespace Northstar.Infrastructure.Storage;

/// <summary>
/// Private Amazon S3 implementation of <see cref="IBlobStorage"/>. Credentials are
/// supplied by the AWS default credential chain (the ECS task role in AWS), so no
/// access keys are stored in configuration. Uploads use SSE-S3 and downloads are
/// exposed only through short-lived pre-signed URLs.
/// </summary>
public sealed class S3BlobStorage : IBlobStorage
{
    private readonly IAmazonS3 _client;
    private readonly string _bucketName;

    public S3BlobStorage(IAmazonS3 client, string bucketName)
    {
        if (string.IsNullOrWhiteSpace(bucketName))
        {
            throw new InvalidOperationException(
                "Storage:S3BucketName must be configured when Storage:Provider is S3.");
        }

        _client = client ?? throw new ArgumentNullException(nameof(client));
        _bucketName = bucketName;
    }

    public async Task<StoredBlob> SaveAsync(
        Stream content,
        string storageKey,
        string contentType,
        CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes));

        await using var upload = new MemoryStream(bytes, writable: false);
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = storageKey,
            InputStream = upload,
            ContentType = contentType,
            ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
        }, ct);

        return new StoredBlob(storageKey, bytes.Length, hash);
    }

    public async Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct)
    {
        var response = await _client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = _bucketName,
            Key = storageKey
        }, ct);

        return response.ResponseStream;
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct) =>
        throw new NotSupportedException(
            "Document deletion is not implemented and the task role deliberately has no s3:DeleteObject permission.");

    public async Task<Uri?> GetPresignedUrlAsync(string storageKey, TimeSpan ttl, CancellationToken ct)
    {
        var url = await _client.GetPreSignedURLAsync(new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = storageKey,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(ttl),
            Protocol = Protocol.HTTPS
        });

        return new Uri(url);
    }
}
