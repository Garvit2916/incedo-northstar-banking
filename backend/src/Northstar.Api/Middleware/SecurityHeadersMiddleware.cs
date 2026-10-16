namespace Northstar.Api.Middleware;

/// <summary>
/// Applies conservative security response headers to every response. A relaxed
/// content-security-policy is used for the Swagger UI route, which requires
/// inline assets, and a strict policy elsewhere.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["X-Permitted-Cross-Domain-Policies"] = "none";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";

        var isSwagger = context.Request.Path.StartsWithSegments("/swagger");
        headers["Content-Security-Policy"] = isSwagger
            ? "default-src 'self'; img-src 'self' data: https:; script-src 'self' 'unsafe-inline' https:; style-src 'self' 'unsafe-inline' https:"
            : "default-src 'none'; frame-ancestors 'none'";

        await _next(context);
    }
}
