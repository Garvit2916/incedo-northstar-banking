using Northstar.Domain;

namespace Northstar.Domain.Workflow;

/// <summary>
/// Authoritative definition of the application lifecycle. All status changes in
/// the system must be expressed as a transition validated here; services never
/// assign <see cref="ApplicationStatus"/> values directly.
/// </summary>
public static class ApplicationStateMachine
{
    private static readonly IReadOnlyDictionary<ApplicationStatus, ApplicationStatus[]> Transitions =
        new Dictionary<ApplicationStatus, ApplicationStatus[]>
        {
            [ApplicationStatus.Draft] = new[] { ApplicationStatus.Submitted, ApplicationStatus.Withdrawn },
            [ApplicationStatus.Submitted] = new[] { ApplicationStatus.UnderReview, ApplicationStatus.Withdrawn },
            [ApplicationStatus.UnderReview] = new[]
            {
                ApplicationStatus.Approved,
                ApplicationStatus.Rejected,
                ApplicationStatus.AdditionalInformationRequired
            },
            [ApplicationStatus.AdditionalInformationRequired] = new[]
            {
                ApplicationStatus.Submitted,
                ApplicationStatus.Withdrawn
            },
            [ApplicationStatus.Approved] = Array.Empty<ApplicationStatus>(),
            [ApplicationStatus.Rejected] = Array.Empty<ApplicationStatus>(),
            [ApplicationStatus.Withdrawn] = Array.Empty<ApplicationStatus>()
        };

    public static bool CanTransition(ApplicationStatus from, ApplicationStatus to) =>
        Transitions.TryGetValue(from, out var allowed) && allowed.Contains(to);

    public static IReadOnlyCollection<ApplicationStatus> AllowedFrom(ApplicationStatus from) =>
        Transitions.TryGetValue(from, out var allowed) ? allowed : Array.Empty<ApplicationStatus>();

    public static bool IsTerminal(ApplicationStatus status) =>
        Transitions.TryGetValue(status, out var allowed) && allowed.Length == 0;

    /// <summary>Throws when a transition is not permitted by the lifecycle.</summary>
    public static void EnsureTransition(ApplicationStatus from, ApplicationStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new DomainException(
                $"Invalid application status transition: '{from}' -> '{to}' is not permitted.");
        }
    }
}
