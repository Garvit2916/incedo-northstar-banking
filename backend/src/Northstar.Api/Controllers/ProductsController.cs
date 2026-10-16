using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Northstar.Application.Contracts;
using Northstar.Application.Services;
using Northstar.Domain;

namespace Northstar.Api.Controllers;

[ApiController]
[Route("api/products")]
[Authorize]
public sealed class ProductsController(ProductService products) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ProductResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ProductResponse>>> List(
        [FromQuery] bool includeInactive,
        CancellationToken ct)
    {
        var isStaff = User.IsInRole(Roles.Reviewer) || User.IsInRole(Roles.Administrator);
        var result = includeInactive && isStaff
            ? await products.ListAllAsync(ct)
            : await products.ListActiveAsync(ct);

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductResponse>> GetById(Guid id, CancellationToken ct)
        => Ok(await products.GetByIdAsync(id, ct));
}
