using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Northstar.Application.Contracts;
using Northstar.Application.Services;
using Northstar.Domain;

namespace Northstar.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public sealed class DashboardController(DashboardService dashboard) : ControllerBase
{
    [HttpGet("customer")]
    [Authorize(Roles = Roles.Customer)]
    [ProducesResponseType(typeof(DashboardStatsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardStatsResponse>> Customer(CancellationToken ct)
        => Ok(await dashboard.GetCustomerStatsAsync(ct));

    [HttpGet("operations")]
    [Authorize(Roles = Roles.SupportAgent + "," + Roles.Reviewer + "," + Roles.Administrator)]
    [ProducesResponseType(typeof(DashboardStatsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardStatsResponse>> Operations(CancellationToken ct)
        => Ok(await dashboard.GetOperationsStatsAsync(ct));
}
