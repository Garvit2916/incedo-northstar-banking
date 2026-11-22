using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Northstar.Application.Common;
using Northstar.Application.Contracts;
using Northstar.Application.Services;
using Northstar.Domain;

namespace Northstar.Api.Controllers;

/// <summary>
/// Customer self-service support requests. Ownership is derived from the token;
/// these endpoints never accept a customer id from the caller.
/// </summary>
[ApiController]
[Route("api/customer/tickets")]
[Authorize(Roles = Roles.Customer)]
public sealed class CustomerTicketsController(TicketService tickets) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(CustomerTicketSummaryResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CustomerTicketSummaryResponse>> Create(
        CreateCustomerTicketRequest request,
        CancellationToken ct)
    {
        var created = await tickets.CreateForCustomerAsync(request, ct);
        return CreatedAtAction(nameof(GetMine), new { id = created.Id }, created);
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<CustomerTicketSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<CustomerTicketSummaryResponse>>> ListMine(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await tickets.ListMineAsync(new PageQuery(page, pageSize), ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CustomerTicketDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerTicketDetailResponse>> GetMine(Guid id, CancellationToken ct)
        => Ok(await tickets.GetMineAsync(id, ct));
}
