using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Northstar.Application.Contracts;
using Northstar.Application.Services;
using Northstar.Domain;

namespace Northstar.Api.Controllers;

[ApiController]
[Route("api/applications")]
[Authorize(Roles = Roles.Customer)]
public sealed class ApplicationsController(ApplicationService applications) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(ApplicationDetailResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApplicationDetailResponse>> Create(
        CreateApplicationRequest request,
        CancellationToken ct)
    {
        var created = await applications.CreateDraftAsync(request, ct);
        return CreatedAtAction(nameof(GetMine), new { id = created.Id }, created);
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ApplicationSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ApplicationSummaryResponse>>> ListMine(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await applications.ListMineAsync(new PageQuery(page, pageSize), ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApplicationDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApplicationDetailResponse>> GetMine(Guid id, CancellationToken ct)
        => Ok(await applications.GetMineAsync(id, ct));

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(ApplicationDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApplicationDetailResponse>> UpdateDraft(
        Guid id,
        UpdateApplicationDraftRequest request,
        CancellationToken ct)
        => Ok(await applications.UpdateDraftAsync(id, request, ct));

    [HttpPost("{id:guid}/submit")]
    [ProducesResponseType(typeof(ApplicationDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApplicationDetailResponse>> Submit(Guid id, CancellationToken ct)
        => Ok(await applications.SubmitAsync(id, ct));

    [HttpPost("{id:guid}/withdraw")]
    [ProducesResponseType(typeof(ApplicationDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApplicationDetailResponse>> Withdraw(Guid id, CancellationToken ct)
        => Ok(await applications.WithdrawAsync(id, ct));

    [HttpGet("{id:guid}/history")]
    [ProducesResponseType(typeof(IReadOnlyList<ApplicationStatusHistoryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ApplicationStatusHistoryResponse>>> History(
        Guid id,
        CancellationToken ct)
        => Ok(await applications.GetMineHistoryAsync(id, ct));
}
