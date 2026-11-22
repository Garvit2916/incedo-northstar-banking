namespace Northstar.Application.Contracts;

/// <summary>
/// A synthetic account view for the customer dashboard. It contains no real
/// banking data and never exposes a full account identifier.
/// </summary>
public sealed record AccountOverviewResponse(
    string AccountName,
    string AccountType,
    string MaskedAccountNumber,
    string MaskedIfsc,
    string Branch,
    string Currency,
    decimal AvailableBalance,
    IReadOnlyList<AccountTransactionResponse> Transactions,
    IReadOnlyList<ActiveProductResponse> ActiveProducts,
    string DemoNotice);

public sealed record AccountTransactionResponse(
    string Reference,
    DateTimeOffset PostedAtUtc,
    string Narration,
    string Category,
    decimal Amount,
    string Direction);

public sealed record ActiveProductResponse(
    string Name,
    string ProductCode,
    string Reference,
    string Status);
