using SmartPlagiarism.Core.Enums;

namespace SmartPlagiarism.Core.DTOs.Reports;

/// <summary>
/// Everything a submission's originality report shows, assembled from the
/// database, so the PDF generator only lays out what it is given.
/// </summary>
/// <param name="ForStaff">
/// A teacher's copy names the matched students and quotes their text. A
/// student's copy does neither: it says a passage matched another submission,
/// never whose, and shows only the student's own words.
/// </param>
public sealed record SubmissionReportData(
    string InstitutionName,
    string DepartmentName,
    bool ForStaff,
    int SubmissionId,
    string Title,
    SubmissionType Type,
    string CourseCode,
    string CourseTitle,
    string SemesterName,
    DateTime SubmittedAtUtc,
    string StudentName,
    string StudentNumber,
    AnalysisStatus AnalysisStatus,
    string? AnalysisError,
    decimal? OverallSimilarityPercent,
    decimal CommonTextPercent,
    DateTime? AnalyzedAtUtc,
    string? EngineVersion,
    IReadOnlyList<ReportFileLine> Files,
    IReadOnlyList<ReportMatchLine> Matches,
    IReadOnlyList<ReportExcerpt> TopExcerpts,
    SubmissionStatus ReviewStatus,
    string? TeacherComment,
    DateTime GeneratedAtUtc);

/// <param name="ReadBy">How the text was read - embedded text, OCR - or null if never read.</param>
/// <param name="Warnings">Why a score for this file may be low.</param>
public sealed record ReportFileLine(string FileName, ExtractionMethod? ReadBy, int? WordCount, IReadOnlyList<string> Warnings);

/// <param name="Source">Who and what was matched - anonymised in a student's copy.</param>
public sealed record ReportMatchLine(string Source, string OwnFileName, decimal SimilarityPercent, int PassageCount);

/// <summary>One matched passage, quoted from both documents.</summary>
/// <param name="SourceExcerpt">The matched source's text; null in a student's copy.</param>
public sealed record ReportExcerpt(string Source, decimal SimilarityPercent, string SubmittedExcerpt, string? SourceExcerpt);

/// <summary>A generated file, ready to send to the browser.</summary>
public sealed record ReportFile(string FileName, string ContentType, byte[] Content);
