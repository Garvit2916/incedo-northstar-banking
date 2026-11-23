namespace Northstar.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>"Local" or "S3".</summary>
    public string Provider { get; set; } = "Local";

    public string LocalRootPath { get; set; } = "App_Data/kyc";

    /// <summary>
    /// Private S3 bucket for KYC documents. Credentials come from the AWS default
    /// credential chain (the ECS task role in AWS); never configure keys here.
    /// </summary>
    public string S3BucketName { get; set; } = string.Empty;

    /// <summary>
    /// Optional region override. When empty the AWS SDK uses its normal
    /// environment/IMDS configuration, which is preferred on ECS Fargate.
    /// </summary>
    public string S3Region { get; set; } = string.Empty;
}
