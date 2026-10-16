using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Northstar.Application.Abstractions;

namespace Northstar.Infrastructure.Identity;

/// <summary>
/// Ambient caller identity derived from the validated JWT principal. Values are
/// read from claims only; nothing here trusts client-supplied identifiers.
/// </summary>
public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUser(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            var value = Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                        ?? Principal?.FindFirstValue("sub");
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public string? Email =>
        Principal?.FindFirstValue(ClaimTypes.Email)
        ?? Principal?.FindFirstValue("email");

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public bool IsInRole(string role) => Principal?.IsInRole(role) ?? false;

    public string? IpAddress => _accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent =>
        _accessor.HttpContext?.Request.Headers.UserAgent.ToString();

    public string? CorrelationId =>
        _accessor.HttpContext?.TraceIdentifier;
}
