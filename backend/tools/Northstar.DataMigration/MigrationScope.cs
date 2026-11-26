using Northstar.Domain;

namespace Northstar.DataMigration;

/// <summary>
/// Fixed, reviewed scope for the one-time original-account migration.
/// These identifiers are data, not credentials, and are stored to preserve the
/// original records exactly.
/// </summary>
public static class MigrationScope
{
    public const string PostgresMigrationId = "20261125102100_InitialPostgres";
    public const string MigrationAction = "migration.original-accounts.applied";

    public static readonly Guid AdministratorUserId = Guid.Parse("6ED5445A-304E-4AB8-AAF5-49AC286A54F1");
    public static readonly Guid CustomerUserId = Guid.Parse("1D6D9FA4-E290-4F9B-9679-A60B35993AD5");
    public static readonly Guid SupportAgentUserId = Guid.Parse("638C2C75-6AB8-448B-AF76-7D88A8492FB9");
    public static readonly Guid ReviewerUserId = Guid.Parse("14984A0B-555B-47E3-9E53-2E580D6CD488");

    /// <summary>
    /// Required by hard foreign keys through CustomerProfiles.UserId and
    /// Notifications.UserId. This account is not one of the four original
    /// accounts but must be present to preserve their related records.
    /// </summary>
    public static readonly Guid RequiredLinkedUserId = Guid.Parse("E7C90EF7-BD2B-48AA-BC70-83FD06B834E4");

    /// <summary>
    /// The AWS-created account that currently owns the administrator email and
    /// username. It is quarantined by renaming, never deleted or overwritten.
    /// </summary>
    public static readonly Guid ConflictAdminUserId = Guid.Parse("2bbaf7f4-63a7-463c-9eab-784c689ef69a");

    public const string AdminEmail = "admin@northstar.example";

    private static readonly Guid OptionalLinkedAgent1 = Guid.Parse("01CC0431-3C34-4393-868C-8EA9442E6204");
    private static readonly Guid OptionalLinkedAgent2 = Guid.Parse("5D538EE3-5238-4C93-80A4-2E36B86BE173");
    private static readonly Guid OptionalLinkedAgent3 = Guid.Parse("852E750A-67D6-40C7-9A9F-933E9124FF03");
    private static readonly Guid OptionalLinkedAgent4 = Guid.Parse("A9B519F7-9444-461B-A83D-E49161866A7D");
    private static readonly Guid OptionalLinkedAgent5 = Guid.Parse("D8A3964A-4C5F-4205-93E0-77182D2BE799");
    private static readonly Guid OptionalLinkedAgent6 = Guid.Parse("D8D7803C-5724-43D2-A391-FED98B389251");

    public static IReadOnlySet<Guid> OriginalUserIds { get; } = new HashSet<Guid>
    {
        AdministratorUserId,
        CustomerUserId,
        SupportAgentUserId,
        ReviewerUserId
    };

    public static IReadOnlySet<Guid> ApprovedUserIds { get; } = new HashSet<Guid>(OriginalUserIds)
    {
        RequiredLinkedUserId
    };

    public static IReadOnlySet<Guid> OptionalLinkedAgentUserIds { get; } = new HashSet<Guid>
    {
        OptionalLinkedAgent1,
        OptionalLinkedAgent2,
        OptionalLinkedAgent3,
        OptionalLinkedAgent4,
        OptionalLinkedAgent5,
        OptionalLinkedAgent6
    };

    public static IReadOnlyDictionary<Guid, IReadOnlySet<string>> ExpectedRolesByUserId { get; } =
        new Dictionary<Guid, IReadOnlySet<string>>
        {
            [AdministratorUserId] = new HashSet<string> { Roles.Administrator },
            [CustomerUserId] = new HashSet<string> { Roles.Customer },
            [SupportAgentUserId] = new HashSet<string> { Roles.SupportAgent },
            [ReviewerUserId] = new HashSet<string> { Roles.Reviewer },
            [RequiredLinkedUserId] = new HashSet<string> { Roles.Customer }
        };

    public static string? CanonicalizeRoleName(string? roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return null;
        }

        var trimmed = roleName.Trim();
        if (string.Equals(trimmed, Roles.Customer, StringComparison.OrdinalIgnoreCase))
        {
            return Roles.Customer;
        }

        if (string.Equals(trimmed, Roles.SupportAgent, StringComparison.OrdinalIgnoreCase))
        {
            return Roles.SupportAgent;
        }

        if (string.Equals(trimmed, Roles.Reviewer, StringComparison.OrdinalIgnoreCase))
        {
            return Roles.Reviewer;
        }

        if (string.Equals(trimmed, Roles.Administrator, StringComparison.OrdinalIgnoreCase))
        {
            return Roles.Administrator;
        }

        return null;
    }

    public static IReadOnlySet<Guid> GetAllowedUserIds(bool includeOptionalLinkedAgentUsers)
    {
        var allowed = new HashSet<Guid>(ApprovedUserIds);
        if (includeOptionalLinkedAgentUsers)
        {
            allowed.UnionWith(OptionalLinkedAgentUserIds);
        }

        return allowed;
    }
}
