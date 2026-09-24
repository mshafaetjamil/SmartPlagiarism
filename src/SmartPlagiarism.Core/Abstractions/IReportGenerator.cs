using SmartPlagiarism.Core.DTOs.Reports;

namespace SmartPlagiarism.Core.Abstractions;

/// <summary>
/// Lays out a report as a document. Pure presentation: it is given all the data
/// and decides nothing about who may see it.
/// </summary>
public interface IReportGenerator
{
    /// <returns>The report as PDF bytes.</returns>
    byte[] GenerateSubmissionReport(SubmissionReportData data);
}

/// <summary>Assembles and generates reports, enforcing who may have them.</summary>
public interface IReportService
{
    /// <summary>
    /// The submission's originality report, or null when it does not exist or the
    /// viewer may not see it - the two are deliberately indistinguishable. Checks
    /// access itself through <see cref="ISubmissionAccessService"/> rather than
    /// trusting its caller to have done so.
    /// </summary>
    Task<ReportFile?> GetSubmissionReportAsync(
        int submissionId,
        SubmissionViewer viewer,
        CancellationToken cancellationToken = default);
}
