namespace Northstar.Domain;

/// <summary>
/// Raised when a domain invariant or business rule is violated. The API layer
/// maps this to HTTP 409 Conflict (or 400 when the cause is caller input).
/// Messages are safe to surface to authenticated callers.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }

    public DomainException(string message, Exception inner) : base(message, inner)
    {
    }
}
