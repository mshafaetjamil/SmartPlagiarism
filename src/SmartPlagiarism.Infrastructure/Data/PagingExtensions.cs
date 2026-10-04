using Microsoft.EntityFrameworkCore;
using SmartPlagiarism.Core.DTOs;

namespace SmartPlagiarism.Infrastructure.Data;

/// <summary>Reads one page of a list in two queries: a count, then the page's rows.</summary>
internal static class PagingExtensions
{
    /// <summary>
    /// The requested page, clamped to the pages that exist, so a stale link past
    /// the end shows the last page rather than an empty one.
    /// </summary>
    /// <param name="query">
    /// Already filtered, ordered and projected. The ordering must end in a unique
    /// key, or rows that tie could appear on two pages or on none.
    /// </param>
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var totalCount = await query.CountAsync(cancellationToken);
        var current = PagedResult<T>.ClampPage(page, totalCount, pageSize);

        var items = await query
            .Skip((current - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<T>(items, current, pageSize, totalCount);
    }
}
