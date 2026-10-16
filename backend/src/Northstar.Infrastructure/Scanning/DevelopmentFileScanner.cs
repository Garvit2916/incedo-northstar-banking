using Microsoft.Extensions.Logging;
using Northstar.Application.Abstractions;
using Northstar.Domain;

namespace Northstar.Infrastructure.Scanning;

/// <summary>
/// Development scanner. No antivirus engine is available in the development
/// environment, so uploads are explicitly reported as <see cref="ScanStatus.SkippedDev"/>.
///
/// This class is the documented integration point for a real engine (for example
/// ClamAV over TCP, or a cloud malware-scanning service). Production deployments
/// must replace this registration; the UI and API already distinguish a scanned
/// document from one that was skipped.
/// </summary>
public sealed class DevelopmentFileScanner : IFileScanner
{
    private readonly ILogger<DevelopmentFileScanner> _logger;

    public DevelopmentFileScanner(ILogger<DevelopmentFileScanner> logger)
    {
        _logger = logger;
    }

    public Task<ScanStatus> ScanAsync(Stream content, string contentType, CancellationToken ct)
    {
        _logger.LogWarning(
            "Malware scanning is not configured. Upload of type {ContentType} was accepted with ScanStatus=SkippedDev. " +
            "Configure a scanning provider before production use.",
            contentType);

        return Task.FromResult(ScanStatus.SkippedDev);
    }
}
