using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Northstar.Application.Contracts;
using Northstar.Application.Services;
using Northstar.Domain;

namespace Northstar.Api.Controllers;

[ApiController]
[Route("api")]
[Authorize]
public sealed class DocumentsController(DocumentService documents) : ControllerBase
{
    private const string OwnerOrStaff = Roles.Customer + "," + Roles.Reviewer + "," + Roles.Administrator;

    [HttpPost("applications/{applicationId:guid}/documents")]
    [Authorize(Roles = Roles.Customer)]
    [EnableRateLimiting("uploads")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    [ProducesResponseType(typeof(DocumentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DocumentResponse>> Upload(
        Guid applicationId,
        [FromForm] UploadDocumentRequest request,
        CancellationToken ct)
    {
        var file = request.File;
        if (file is null || file.Length == 0)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid upload", detail: "A non-empty file is required.");
        }

        await using var stream = file.OpenReadStream();
        var upload = new DocumentUpload(
            file.FileName,
            file.ContentType ?? "application/octet-stream",
            file.Length,
            stream,
            request.DocumentType);

        var created = await documents.UploadAsync(applicationId, upload, ct);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpGet("applications/{applicationId:guid}/documents")]
    [Authorize(Roles = OwnerOrStaff)]
    [ProducesResponseType(typeof(IReadOnlyList<DocumentResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DocumentResponse>>> List(Guid applicationId, CancellationToken ct)
        => Ok(await documents.ListForApplicationAsync(applicationId, ct));

    [HttpPost("documents/{id:guid}/review")]
    [Authorize(Roles = Roles.Reviewer + "," + Roles.Administrator)]
    [ProducesResponseType(typeof(DocumentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DocumentResponse>> Review(
        Guid id,
        DocumentReviewRequest request,
        CancellationToken ct)
        => Ok(await documents.ReviewAsync(id, request, ct));

    [HttpGet("documents/{id:guid}/link")]
    [Authorize(Roles = OwnerOrStaff)]
    [ProducesResponseType(typeof(DownloadLinkResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<DownloadLinkResponse>> GetDownloadLink(Guid id, CancellationToken ct)
        => Ok(await documents.GetDownloadLinkAsync(id, ct));

    [HttpGet("documents/{id:guid}/content")]
    [Authorize(Roles = OwnerOrStaff)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Content(Guid id, CancellationToken ct)
    {
        var content = await documents.OpenContentAsync(id, ct);
        return File(content.Content, content.ContentType, content.FileName);
    }
}

/// <summary>
/// Multipart/form-data payload for a KYC document upload. Modelling the form as
/// a bound type (rather than placing <c>[FromForm]</c> directly on an
/// <see cref="IFormFile"/> parameter) lets Swashbuckle generate a valid schema.
/// </summary>
public sealed class UploadDocumentRequest
{
    public IFormFile File { get; set; } = default!;

    public KycDocumentType DocumentType { get; set; }
}
