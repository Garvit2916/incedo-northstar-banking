using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Northstar.Application.Contracts;
using Northstar.Application.Services;
using Northstar.Domain;

namespace Northstar.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = Roles.Administrator)]
public sealed class AdminController(
    AdminService admin,
    ProductService products) : ControllerBase
{
    [HttpGet("users")]
    [ProducesResponseType(typeof(IReadOnlyList<UserSummary>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UserSummary>>> ListUsers(CancellationToken ct)
        => Ok(await admin.ListUsersAsync(ct));

    [HttpPut("users/{id:guid}/roles")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignRoles(Guid id, AssignRolesRequest request, CancellationToken ct)
    {
        await admin.AssignRolesAsync(id, request, ct);
        return NoContent();
    }

    [HttpPut("users/{id:guid}/active")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetActive(Guid id, SetUserActiveRequest request, CancellationToken ct)
    {
        await admin.SetActiveAsync(id, request, ct);
        return NoContent();
    }

    [HttpGet("audit")]
    [ProducesResponseType(typeof(PagedResult<AuditEventResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AuditEventResponse>>> Audit(
        [FromQuery] string? action,
        [FromQuery] Guid? actorUserId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
        => Ok(await admin.ListAuditEventsAsync(action, actorUserId, new PageQuery(page, pageSize), ct));

    [HttpPost("products")]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProductResponse>> CreateProduct(
        UpsertProductRequest request,
        CancellationToken ct)
    {
        var created = await products.CreateAsync(request, ct);
        return CreatedAtAction("GetById", "Products", new { id = created.Id }, created);
    }

    [HttpPut("products/{id:guid}")]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductResponse>> UpdateProduct(
        Guid id,
        UpsertProductRequest request,
        CancellationToken ct)
        => Ok(await products.UpdateAsync(id, request, ct));
}
