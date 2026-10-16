namespace Northstar.Application.Common;

/// <summary>Requested resource does not exist, or the caller is not permitted to know it exists.</summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }
}

/// <summary>The caller is authenticated but not permitted to perform the operation.</summary>
public sealed class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message)
    {
    }
}

/// <summary>The request conflicts with the current state of the resource.</summary>
public sealed class ConflictException : Exception
{
    public ConflictException(string message) : base(message)
    {
    }
}

/// <summary>
/// The request payload is syntactically or semantically invalid and cannot be
/// accepted (for example an unsupported upload type or a malformed document).
/// Maps to HTTP 400, in contrast to <see cref="ConflictException"/> (HTTP 409).
/// </summary>
public sealed class RequestValidationException : Exception
{
    public RequestValidationException(string message) : base(message)
    {
    }
}
