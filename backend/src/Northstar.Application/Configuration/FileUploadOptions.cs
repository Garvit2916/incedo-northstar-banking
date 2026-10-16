namespace Northstar.Application.Configuration;

/// <summary>
/// Server-side document upload policy. These values are authoritative: the
/// browser's declared content type and file extension are treated as untrusted
/// hints and re-validated against this policy plus a content signature check.
/// </summary>
public sealed class FileUploadOptions
{
    public const string SectionName = "FileUpload";

    /// <summary>
    /// Policy used when the <c>FileUpload</c> config section declares no
    /// extensions or content types.
    /// </summary>
    private static readonly string[] FallbackExtensions =
    [
        ".pdf",
        ".jpg",
        ".jpeg",
        ".png"
    ];

    private static readonly string[] FallbackContentTypes =
    [
        "application/pdf",
        "image/jpeg",
        "image/png"
    ];

    // The configuration binder appends configured entries onto an existing
    // non-null array instead of replacing it, so a non-empty default would merge
    // with configuration and could never be narrowed or cleared by config. These
    // collections therefore start empty; ApplyFallbacks supplies the default
    // policy only when configuration left them unset.
    private string[] _allowedExtensions = [];

    private string[] _allowedContentTypes = [];

    public long MaxFileSizeBytes { get; set; } = 5 * 1024 * 1024;

    public string[] AllowedExtensions
    {
        get => _allowedExtensions;
        set => _allowedExtensions = value ?? [];
    }

    public string[] AllowedContentTypes
    {
        get => _allowedContentTypes;
        set => _allowedContentTypes = value ?? [];
    }

    /// <summary>Replaces any empty allow-list with the built-in fallback policy.</summary>
    public void ApplyFallbacks()
    {
        if (_allowedExtensions.Length == 0)
        {
            _allowedExtensions = FallbackExtensions;
        }

        if (_allowedContentTypes.Length == 0)
        {
            _allowedContentTypes = FallbackContentTypes;
        }
    }
}
