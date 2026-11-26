using Northstar.Domain.Entities;

namespace Northstar.DataMigration.Tests;

public class S3BlobVerifierTests
{
    private const string ExpectedHash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string DifferentHash = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    [Fact]
    public async Task ReportsMissingAndMismatchedObjectsUsingOnlyReadOperations()
    {
        var documents = new[]
        {
            Document("kyc/ok", 4, ExpectedHash),
            Document("kyc/missing", 4, ExpectedHash),
            Document("kyc/size-mismatch", 5, ExpectedHash),
            Document("kyc/hash-mismatch", 4, ExpectedHash)
        };
        var reader = new FakeS3BlobReader(
            new Dictionary<string, long?>
            {
                ["kyc/ok"] = 4,
                ["kyc/missing"] = null,
                ["kyc/size-mismatch"] = 6,
                ["kyc/hash-mismatch"] = 4
            },
            new Dictionary<string, string?>
            {
                ["kyc/ok"] = ExpectedHash,
                ["kyc/hash-mismatch"] = DifferentHash
            });
        var options = CreateOptions();

        var summary = await new S3BlobVerifier(reader).VerifyAsync(documents, options);

        Assert.True(summary.Enabled);
        Assert.Equal(4, summary.CheckedCount);
        Assert.Equal(1, summary.ChecksumVerifiedCount);
        Assert.Equal(1, summary.MissingCount);
        Assert.Equal(2, summary.MismatchCount);
        Assert.Equal(new[] { "kyc/missing" }, summary.MissingKeys);
        Assert.Contains(summary.Mismatches, mismatch =>
            mismatch.StorageKey == "kyc/size-mismatch" && mismatch.Kind == "size");
        Assert.Contains(summary.Mismatches, mismatch =>
            mismatch.StorageKey == "kyc/hash-mismatch" && mismatch.Kind == "sha256");
        Assert.Contains(summary.Blockers, blocker =>
            blocker.Contains("is missing", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(summary.Blockers, blocker =>
            blocker.Contains("SHA-256 does not match", StringComparison.Ordinal));
        Assert.Equal(
            new[]
            {
                "head:kyc/ok",
                "get:kyc/ok",
                "head:kyc/missing",
                "head:kyc/size-mismatch",
                "head:kyc/hash-mismatch",
                "get:kyc/hash-mismatch"
            },
            reader.Calls);
    }

    [Fact]
    public async Task MissingBucketSkipsVerificationWithoutTouchingS3()
    {
        var reader = new FakeS3BlobReader(
            new Dictionary<string, long?>(),
            new Dictionary<string, string?>());
        var options = MigrationOptions.Parse(new[]
        {
            "--source", "some.db",
            "--target", "Host=localhost;Database=test"
        });

        var summary = await new S3BlobVerifier(reader).VerifyAsync(
            new[] { Document("kyc/one", 4, ExpectedHash) },
            options);

        Assert.False(summary.Enabled);
        Assert.Contains("skipped", summary.Warning, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(reader.Calls);
    }

    private static MigrationOptions CreateOptions() => MigrationOptions.Parse(new[]
    {
        "--source", "some.db",
        "--target", "Host=localhost;Database=test",
        "--s3-bucket", "private-kyc"
    });

    private static KycDocument Document(string storageKey, long sizeBytes, string sha256) => new()
    {
        StorageKey = storageKey,
        SizeBytes = sizeBytes,
        Sha256 = sha256,
        ContentType = "application/octet-stream"
    };

    private sealed class FakeS3BlobReader : IS3BlobReader
    {
        private readonly IReadOnlyDictionary<string, long?> _sizes;
        private readonly IReadOnlyDictionary<string, string?> _hashes;

        public FakeS3BlobReader(
            IReadOnlyDictionary<string, long?> sizes,
            IReadOnlyDictionary<string, string?> hashes)
        {
            _sizes = sizes;
            _hashes = hashes;
        }

        public List<string> Calls { get; } = new();

        public Task<long?> TryGetSizeAsync(
            string bucketName,
            string key,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"head:{key}");
            return Task.FromResult(_sizes.GetValueOrDefault(key));
        }

        public Task<string?> TryComputeSha256Async(
            string bucketName,
            string key,
            CancellationToken cancellationToken = default)
        {
            Calls.Add($"get:{key}");
            return Task.FromResult(_hashes.GetValueOrDefault(key));
        }
    }
}
