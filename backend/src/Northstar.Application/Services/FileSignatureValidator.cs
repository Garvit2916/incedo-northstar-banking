namespace Northstar.Application.Services;

/// <summary>
/// Inspects the leading bytes of an upload to confirm the declared content type
/// matches the actual file signature. The browser-supplied MIME type and file
/// extension are treated as untrusted hints only.
/// </summary>
public static class FileSignatureValidator
{
    private static readonly byte[] Pdf = { 0x25, 0x50, 0x44, 0x46 }; // %PDF
    private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    public static bool Matches(string contentOrExtension, ReadOnlySpan<byte> header)
    {
        var key = contentOrExtension.ToLowerInvariant();
        return key switch
        {
            "application/pdf" or ".pdf" => header.StartsWith(Pdf),
            "image/jpeg" or ".jpg" or ".jpeg" => header.StartsWith(Jpeg),
            "image/png" or ".png" => header.StartsWith(Png),
            _ => false
        };
    }
}
