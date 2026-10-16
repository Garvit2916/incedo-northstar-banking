using System.Security.Cryptography;
using Northstar.Application.Abstractions;

namespace Northstar.Infrastructure.Common;

/// <summary>
/// Generates non-sequential, human-readable references. Non-sequential values
/// avoid leaking volume information or enabling enumeration of other records.
/// </summary>
public sealed class ReferenceGenerator : IReferenceGenerator
{
    public string NextApplicationReference() => $"NS-APP-{Suffix()}";

    public string NextTicketReference() => $"NS-TKT-{Suffix()}";

    private static string Suffix() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
}
