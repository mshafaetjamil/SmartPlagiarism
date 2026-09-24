namespace SmartPlagiarism.Web.Models;

/// <summary>
/// Previous / next links for a paged list. <see cref="RouteFor"/> keeps the
/// list's filters in each link, so paging never loses them.
/// </summary>
public sealed record PagerViewModel(string Action, int Page, int TotalPages, Func<int, Dictionary<string, string>> RouteFor);
