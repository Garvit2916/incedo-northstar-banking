namespace Northstar.Infrastructure.Storage;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>"Local" (development) or "Azure" (Blob Storage).</summary>
    public string Provider { get; set; } = "Local";

    public string LocalRootPath { get; set; } = "App_Data/kyc";

    public string? AzureConnectionString { get; set; }

    public string AzureContainerName { get; set; } = "kyc-documents";
}
