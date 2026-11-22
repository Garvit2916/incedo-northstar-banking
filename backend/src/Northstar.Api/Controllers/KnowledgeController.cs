using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Northstar.Application.Contracts;
using Northstar.Application.Services;
using Northstar.Domain;

namespace Northstar.Api.Controllers;

/// <summary>
/// Read-only support knowledge base. Agents can search and read approved demo
/// articles; there is intentionally no write endpoint in the prototype.
/// </summary>
[ApiController]
[Route("api/knowledge")]
[Authorize(Roles = Roles.SupportAgent + "," + Roles.Administrator)]
public sealed class KnowledgeController(KnowledgeService knowledge) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<KnowledgeArticleResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<KnowledgeArticleResponse>>> Search(
        [FromQuery] string? search,
        [FromQuery] string? category,
        [FromQuery] string? product,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await knowledge.SearchAsync(
            new KnowledgeQuery(search, category, product, page, pageSize), ct);
        return Ok(result);
    }

    [HttpGet("categories")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<string>>> Categories(CancellationToken ct)
        => Ok(await knowledge.CategoriesAsync(ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(KnowledgeArticleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<KnowledgeArticleResponse>> Get(Guid id, CancellationToken ct)
        => Ok(await knowledge.GetAsync(id, ct));
}
