using Northstar.Application.Abstractions;

namespace Northstar.Infrastructure.Common;

/// <summary>Real clock, backed by <see cref="TimeProvider"/> for testability.</summary>
public sealed class SystemDateTimeProvider : IDateTimeProvider
{
    private readonly TimeProvider _timeProvider;

    public SystemDateTimeProvider(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();
}
