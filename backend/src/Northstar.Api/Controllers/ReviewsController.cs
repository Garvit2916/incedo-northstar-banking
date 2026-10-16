using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Northstar.Application.Contracts;
using Northstar.Application.Services;
using Northstar.Domain;

namespace Northstar.Api.Controllers;

/// <summary>
/// Reviewer-facing operations. Access is restricted to reviewers and administrators;
/// individual applications are re-authorised server-side inside the service layer.
/// </summary>
[ApiController]
[Route("api/reviews")]
[Authorize(Roles = Roles.Reviewer + "," + Roles.Administrator)]
public sealed class ReviewsController(ApplicationService applications) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ApplicationSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ApplicationSummaryResponse>>> Queue(
        [FromQuery] ApplicationStatus? status,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await applications.ListReviewQueueAsync(status, search, new PageQuery(page, pageSize), ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApplicationDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApplicationDetailResponse>> Get(Guid id, CancellationToken ct)
        => Ok(await applications.GetForReviewAsync(id, ct));

    [HttpPost("{id:guid}/start")]
    [ProducesResponseType(typeof(ApplicationDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApplicationDetailResponse>> Start(Guid id, CancellationToken ct)
        => Ok(await applications.StartReviewAsync(id, ct));

    [HttpPost("{id:guid}/request-information")]
    [ProducesResponseType(typeof(ApplicationDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApplicationDetailResponse>> RequestInformation(
        Guid id,
        ReasonRequest request,
        CancellationToken ct)
        => Ok(await applications.RequestAdditionalInformationAsync(id, request, ct));

    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType(typeof(ApplicationDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApplicationDetailResponse>> Approve(
        Guid id,
        ReasonRequest request,
        CancellationToken ct)
        => Ok(await applications.ApproveAsync(id, request, ct));

    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType(typeof(ApplicationDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApplicationDetailResponse>> Reject(
        Guid id,
        ReasonRequest request,
        CancellationToken ct)
        => Ok(await applications.RejectAsync(id, request, ct));
}
