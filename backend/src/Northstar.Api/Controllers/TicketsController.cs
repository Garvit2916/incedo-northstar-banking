using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Northstar.Application.Contracts;
using Northstar.Application.Services;
using Northstar.Domain;

namespace Northstar.Api.Controllers;

/// <summary>
/// AI-assisted customer support workspace. Restricted to support agents and
/// administrators; every AI result carries explicit provenance so the UI can
/// label generated content and flag non-live inference.
/// </summary>
[ApiController]
[Route("api/tickets")]
[Authorize(Roles = Roles.SupportAgent + "," + Roles.Administrator)]
public sealed class TicketsController(
    TicketService tickets,
    AiAssistanceService ai) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<TicketSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<TicketSummaryResponse>>> List(
        [FromQuery] TicketStatus? status,
        [FromQuery] TicketCategory? category,
        [FromQuery] TicketPriority? priority,
        [FromQuery] Guid? assignedAgentId,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var query = new TicketQuery(status, category, priority, assignedAgentId, search, page, pageSize);
        return Ok(await tickets.ListAsync(query, ct));
    }

    [HttpGet("overview")]
    [ProducesResponseType(typeof(TicketOverviewResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<TicketOverviewResponse>> Overview(CancellationToken ct)
        => Ok(await tickets.GetOverviewAsync(ct));

    [HttpPost]
    [ProducesResponseType(typeof(TicketDetailResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TicketDetailResponse>> Create(
        CreateTicketRequest request,
        CancellationToken ct)
    {
        var created = await tickets.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(TicketDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TicketDetailResponse>> Get(Guid id, CancellationToken ct)
        => Ok(await tickets.GetAsync(id, ct));

    [HttpPost("{id:guid}/messages")]
    [ProducesResponseType(typeof(TicketDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TicketDetailResponse>> AddMessage(
        Guid id,
        AddTicketMessageRequest request,
        CancellationToken ct)
        => Ok(await tickets.AddMessageAsync(id, request, ct));

    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(TicketDetailResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<TicketDetailResponse>> ChangeStatus(
        Guid id,
        ChangeTicketStatusRequest request,
        CancellationToken ct)
        => Ok(await tickets.ChangeStatusAsync(id, request, ct));

    [HttpPost("{id:guid}/assign")]
    [ProducesResponseType(typeof(TicketDetailResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<TicketDetailResponse>> Assign(
        Guid id,
        AssignTicketRequest request,
        CancellationToken ct)
        => Ok(await tickets.AssignAsync(id, request, ct));

    [HttpPost("{id:guid}/ai/summarize")]
    [EnableRateLimiting("ai")]
    [ProducesResponseType(typeof(AiSummaryResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AiSummaryResponse>> Summarize(Guid id, CancellationToken ct)
        => Ok(await ai.SummarizeAsync(id, ct));

    [HttpPost("{id:guid}/ai/suggest-response")]
    [EnableRateLimiting("ai")]
    [ProducesResponseType(typeof(AiSuggestedResponseResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AiSuggestedResponseResponse>> SuggestResponse(Guid id, CancellationToken ct)
        => Ok(await ai.SuggestResponseAsync(id, ct));

    [HttpPost("{id:guid}/ai/analyze-sentiment")]
    [EnableRateLimiting("ai")]
    [ProducesResponseType(typeof(AiSentimentResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AiSentimentResponse>> AnalyzeSentiment(Guid id, CancellationToken ct)
        => Ok(await ai.AnalyzeSentimentAsync(id, ct));

    [HttpPost("{id:guid}/ai/next-best-actions")]
    [EnableRateLimiting("ai")]
    [ProducesResponseType(typeof(NextBestActionsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<NextBestActionsResponse>> NextBestActions(Guid id, CancellationToken ct)
        => Ok(await ai.NextBestActionsAsync(id, ct));
}
