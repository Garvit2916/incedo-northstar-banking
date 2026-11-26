using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Northstar.Domain.Entities;
using Northstar.Infrastructure.Identity;
using Northstar.Infrastructure.Persistence;

namespace Northstar.DataMigration;

/// <summary>
/// Produces deterministic hashes of migrated row contents. Password hashes and
/// security stamps are included in the hash input but never written to reports.
/// </summary>
public static class ContentFingerprint
{
    private const char Separator = '\u001f';

    public static Dictionary<string, string> ComputeSource(
        AppDbContext context,
        SourceSnapshot snapshot) => new(StringComparer.Ordinal)
    {
        [MigrationTables.Users] = Compute(context, snapshot.Users),
        [MigrationTables.Products] = Compute(context, snapshot.Products),
        [MigrationTables.CustomerProfiles] = Compute(context, snapshot.CustomerProfiles),
        [MigrationTables.Applications] = Compute(context, snapshot.Applications),
        [MigrationTables.ApplicationStatusHistory] = Compute(context, snapshot.ApplicationStatusHistory),
        [MigrationTables.KycDocuments] = Compute(context, snapshot.KycDocuments),
        [MigrationTables.Tickets] = Compute(context, snapshot.Tickets),
        [MigrationTables.TicketAssignments] = Compute(context, snapshot.TicketAssignments),
        [MigrationTables.TicketMessages] = Compute(context, snapshot.TicketMessages),
        [MigrationTables.AiAssistanceRecords] = Compute(context, snapshot.AiAssistanceRecords),
        [MigrationTables.Notifications] = Compute(context, snapshot.Notifications),
        [MigrationTables.AuditEvents] = Compute(context, snapshot.AuditEvents)
    };

    public static string Compute<TEntity>(
        AppDbContext context,
        IEnumerable<TEntity> entities,
        params string[] excludedProperties)
        where TEntity : class
    {
        var entityType = context.Model.FindEntityType(typeof(TEntity))
                         ?? throw new InvalidOperationException($"Entity type {typeof(TEntity).Name} is not in the model.");
        var excluded = excludedProperties.ToHashSet(StringComparer.Ordinal);
        var properties = entityType.GetProperties()
            .Where(property => property.PropertyInfo is not null && !excluded.Contains(property.Name))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToList();

        var rows = entities
            .Select(entity => string.Join(
                Separator,
                properties.Select(property => Canonicalize(property.PropertyInfo!.GetValue(entity)))))
            .OrderBy(row => row, StringComparer.Ordinal)
            .ToList();

        var builder = new StringBuilder();
        foreach (var row in rows)
        {
            builder.Append(row).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static string Canonicalize(object? value) => value switch
    {
        null => "<null>",
        string text => text,
        Guid guid => guid.ToString("N"),
        DateTimeOffset timestamp => timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        DateTime timestamp => timestamp.ToString("O", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeOnly time => time.ToString("O", CultureInfo.InvariantCulture),
        bool flag => flag ? "true" : "false",
        Enum enumeration => enumeration.ToString(),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        System.Collections.IEnumerable enumerable => JsonSerializer.Serialize(ToList(enumerable)),
        _ => value.ToString() ?? string.Empty
    };

    private static List<object?> ToList(System.Collections.IEnumerable enumerable)
    {
        var result = new List<object?>();
        foreach (var item in enumerable)
        {
            result.Add(item);
        }

        return result;
    }
}
