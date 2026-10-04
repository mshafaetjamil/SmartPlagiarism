using System.Globalization;
using SmartPlagiarism.Core.DTOs;

namespace SmartPlagiarism.Web.Models;

/// <summary>
/// Previous / next links for a paged list. <see cref="RouteFor"/> keeps the
/// list's filters in each link, so paging never loses them.
/// </summary>
public sealed record PagerViewModel(string Action, int Page, int TotalPages, Func<int, Dictionary<string, string>> RouteFor)
{
    /// <summary>For a list with no filters: the page number is all a link carries.</summary>
    public static PagerViewModel For<T>(string action, PagedResult<T> results) => new(
        action,
        results.Page,
        results.TotalPages,
        page => new Dictionary<string, string> { ["page"] = page.ToString(CultureInfo.InvariantCulture) });
}
