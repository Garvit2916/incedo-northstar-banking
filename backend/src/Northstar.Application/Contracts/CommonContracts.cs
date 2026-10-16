namespace Northstar.Application.Contracts;

/// <summary>Standard envelope for paginated collection endpoints.</summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public enum SortDirection
{
    Ascending,
    Descending
}

/// <summary>Query parameters shared by paginated list endpoints.</summary>
public sealed record PageQuery(int Page = 1, int PageSize = 20)
{
    public const int MaxPageSize = 100;

    public int NormalisedPage => Page < 1 ? 1 : Page;

    public int NormalisedPageSize => PageSize switch
    {
        < 1 => 20,
        > MaxPageSize => MaxPageSize,
        _ => PageSize
    };
}
