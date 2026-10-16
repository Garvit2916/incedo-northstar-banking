using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Northstar.Application.Contracts;
using Northstar.Application.Services;

namespace Northstar.Api.Controllers;

[ApiController]
[Route("api/customers")]
[Authorize(Roles = "Customer")]
public sealed class CustomersController(CustomerService customers) : ControllerBase
{
    [HttpGet("me")]
    [ProducesResponseType(typeof(ProfileResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProfileResponse>> GetMyProfile(CancellationToken ct)
        => Ok(await customers.GetMyProfileAsync(ct));

    [HttpPut("me")]
    [ProducesResponseType(typeof(ProfileResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProfileResponse>> UpdateMyProfile(
        UpdateProfileRequest request,
        CancellationToken ct)
        => Ok(await customers.UpdateMyProfileAsync(request, ct));
}
