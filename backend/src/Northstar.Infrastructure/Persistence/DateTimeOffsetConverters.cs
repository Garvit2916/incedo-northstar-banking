using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Northstar.Infrastructure.Persistence;

/// <summary>
/// Persists <see cref="DateTimeOffset"/> as UTC ticks (a 64-bit integer).
///
/// The SQLite provider cannot order or compare <c>DateTimeOffset</c> columns,
/// which breaks paginated list queries and the outbox poller. Storing the value
/// as a <c>long</c> makes ordering and range comparisons translatable on both
/// SQLite and SQL Server. Every timestamp in this system is captured in UTC, so
/// the offset is always zero and UTC ticks preserve chronological ordering.
/// </summary>
public sealed class DateTimeOffsetToUtcTicksConverter : ValueConverter<DateTimeOffset, long>
{
    public DateTimeOffsetToUtcTicksConverter()
        : base(
            value => value.UtcTicks,
            ticks => new DateTimeOffset(ticks, TimeSpan.Zero))
    {
    }
}

/// <summary>Nullable counterpart of <see cref="DateTimeOffsetToUtcTicksConverter"/>.</summary>
public sealed class NullableDateTimeOffsetToUtcTicksConverter : ValueConverter<DateTimeOffset?, long?>
{
    public NullableDateTimeOffsetToUtcTicksConverter()
        : base(
            value => value.HasValue ? value.Value.UtcTicks : null,
            ticks => ticks.HasValue ? new DateTimeOffset(ticks.Value, TimeSpan.Zero) : null)
    {
    }
}
