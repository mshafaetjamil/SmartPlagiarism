using SmartPlagiarism.Core.DTOs;
using SmartPlagiarism.Core.DTOs.Admin;
using SmartPlagiarism.Core.DTOs.Archive;

namespace SmartPlagiarism.Web.Models.Admin;

public sealed class ArchiveFilter
{
    public int? Year { get; set; }

    public int? DepartmentId { get; set; }

    public string? Search { get; set; }

    public int Page { get; set; } = 1;

    public ArchiveQuery ToQuery() => new(Year, DepartmentId, Search, Page);

    public Dictionary<string, string> RouteValues(int page) =>
        QueryValues.From((nameof(Year), Year), (nameof(DepartmentId), DepartmentId), (nameof(Search), Search), (nameof(Page), page));
}

public sealed class ArchiveImportForm
{
    public IFormFile? Package { get; set; }

    public int? DefaultYear { get; set; }

    public int? DefaultDepartmentId { get; set; }
}

public sealed record ArchiveViewModel(
    ArchiveFilter Filter,
    ArchiveOverviewDto Overview,
    PagedResult<ArchiveDocumentDto> Documents,
    IReadOnlyList<DepartmentDto> Departments)
{
    /// <summary>While an import runs the page reloads itself, so its progress moves.</summary>
    public bool ImportRunning => Overview.RecentImports.Any(import => import.IsRunning);
}
