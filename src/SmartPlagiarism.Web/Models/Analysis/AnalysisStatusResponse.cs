using SmartPlagiarism.Core.DTOs.Analysis;

namespace SmartPlagiarism.Web.Models.Analysis;

/// <summary>
/// One entry of the JSON the status endpoints return to the polling script. The
/// status is the enum's name, which is also what the page rendered, so the
/// script compares strings and needs no mapping of its own.
/// </summary>
public sealed record AnalysisStatusResponse(int Id, string Status)
{
    public static IEnumerable<AnalysisStatusResponse> From(IEnumerable<AnalysisStatusDto> statuses) =>
        statuses.Select(status => new AnalysisStatusResponse(status.SubmissionId, status.AnalysisStatus.ToString()));
}
