using SmartPlagiarism.Core.Enums;

namespace SmartPlagiarism.Core.DTOs.Submissions;

/// <summary>One row in a student's submission history.</summary>
public sealed record SubmissionListItemDto(
    int Id,
    string Title,
    string CourseCode,
    string SemesterName,
    SubmissionType Type,
    DateTime SubmittedAtUtc,
    AnalysisStatus AnalysisStatus,
    SubmissionStatus Status,
    int FileCount,
    decimal? OverallSimilarityPercent);

/// <summary>Everything shown on a submission's detail page.</summary>
public sealed record SubmissionDetailDto(
    int Id,
    string Title,
    string CourseCode,
    string CourseTitle,
    string SemesterName,
    SubmissionType Type,
    DateTime SubmittedAtUtc,
    AnalysisStatus AnalysisStatus,
    string? AnalysisError,
    SubmissionStatus Status,
    string? TeacherComment,
    IReadOnlyList<SubmissionFileDto> Files,
    SubmissionResultDto? Result)
{
    /// <summary>
    /// What the analysis matched, worded for the student who owns the submission:
    /// never another person's name or title. Filled for a student viewer only, and
    /// not part of the REST API's response.
    /// </summary>
    public IReadOnlyList<SubmissionSourceDto> Sources { get; init; } = [];
}

public sealed record SubmissionFileDto(
    int Id,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string Sha256Hash,
    ExtractionMethod? ReadBy);

/// <param name="IsOwnEarlierWork">The student's own earlier submission: listed, but not counted in the overall figure.</param>
public sealed record SubmissionSourceDto(
    string Label,
    string OwnFileName,
    int PassageCount,
    decimal SimilarityPercent,
    bool IsOwnEarlierWork);

/// <summary>Null on the detail DTO until an analysis has completed.</summary>
/// <param name="CommonTextPercent">Share set aside as text most of the course shares.</param>
public sealed record SubmissionResultDto(
    decimal OverallSimilarityPercent,
    decimal CommonTextPercent,
    DateTime AnalyzedAtUtc,
    string EngineVersion,
    int MatchCount);

/// <summary>Dropdown contents for the create form.</summary>
public sealed record SubmissionFormOptionsDto(
    IReadOnlyList<CourseOptionDto> Courses,
    IReadOnlyList<SemesterOptionDto> Semesters);

public sealed record CourseOptionDto(int Id, string Code, string Title);

public sealed record SemesterOptionDto(int Id, string Name);
