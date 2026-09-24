namespace SmartPlagiarism.Core.DTOs;

/// <summary>One page of a longer list, with what is needed to link to the others.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    /// <summary>The page to show when <paramref name="requested"/> may be past the end.</summary>
    public static int ClampPage(int requested, int totalCount, int pageSize) =>
        Math.Clamp(requested, 1, Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize)));
}
