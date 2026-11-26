using Northstar.Domain.Entities;

namespace Northstar.DataMigration;

public interface IS3BlobVerifier
{
    Task<BlobVerificationSummary> VerifyAsync(
        IReadOnlyList<KycDocument> documents,
        MigrationOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Verifies KYC objects through <see cref="IS3BlobReader"/>, whose surface is
/// intentionally read-only. This class never uploads, copies, overwrites,
/// deletes, or otherwise modifies S3 objects.
/// </summary>
public sealed class S3BlobVerifier : IS3BlobVerifier
{
    private readonly IS3BlobReader? _reader;

    public S3BlobVerifier(IS3BlobReader? reader = null)
    {
        _reader = reader;
    }

    public async Task<BlobVerificationSummary> VerifyAsync(
        IReadOnlyList<KycDocument> documents,
        MigrationOptions options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(options.S3BucketName))
        {
            return new BlobVerificationSummary
            {
                Enabled = false,
                Warning = "KYC blob verification skipped; no --s3-bucket supplied."
            };
        }

        var reader = _reader ?? S3BlobReader.FromOptions(options);
        var bucket = options.S3BucketName;
        var summary = new BlobVerificationSummary
        {
            Enabled = true,
            Bucket = bucket,
            CheckedCount = documents.Count
        };

        foreach (var document in documents)
        {
            var size = await reader.TryGetSizeAsync(bucket, document.StorageKey, cancellationToken);
            if (size is null)
            {
                summary.MissingKeys.Add(document.StorageKey);
                summary.Blockers.Add(
                    $"S3 object {document.StorageKey} is missing. The utility is read-only and will not create or modify it; resolve the object outside this tool and rerun.");
                continue;
            }

            if (size != document.SizeBytes)
            {
                summary.Mismatches.Add(new BlobMismatch
                {
                    StorageKey = document.StorageKey,
                    Kind = "size",
                    Expected = document.SizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Actual = size.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                });
                summary.Blockers.Add(
                    $"S3 object {document.StorageKey} size {size.Value} does not match KycDocuments.SizeBytes {document.SizeBytes}.");
                continue;
            }

            var actualSha256 = await reader.TryComputeSha256Async(bucket, document.StorageKey, cancellationToken);
            if (actualSha256 is null)
            {
                summary.MissingKeys.Add(document.StorageKey);
                summary.Blockers.Add(
                    $"S3 object {document.StorageKey} disappeared during read-only checksum verification; resolve the object outside this tool and rerun.");
                continue;
            }

            if (!string.Equals(actualSha256, document.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                summary.Mismatches.Add(new BlobMismatch
                {
                    StorageKey = document.StorageKey,
                    Kind = "sha256",
                    Expected = document.Sha256,
                    Actual = actualSha256
                });
                summary.Blockers.Add(
                    $"S3 object {document.StorageKey} SHA-256 does not match KycDocuments.Sha256.");
                continue;
            }

            summary.ChecksumVerifiedCount++;
        }

        summary.MissingCount = summary.MissingKeys.Count;
        summary.MismatchCount = summary.Mismatches.Count;
        return summary;
    }
}
