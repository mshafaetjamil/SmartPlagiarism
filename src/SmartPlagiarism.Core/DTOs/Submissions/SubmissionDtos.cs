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
    SubmissionStatus Status,
    string? TeacherComment,
    IReadOnlyList<SubmissionFileDto> Files,
    SubmissionResultDto? Result);

public sealed record SubmissionFileDto(
    int Id,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string Sha256Hash);

/// <summary>Null on the detail DTO until Phase 6's pipeline produces an analysis.</summary>
public sealed record SubmissionResultDto(
    decimal OverallSimilarityPercent,
    DateTime AnalyzedAtUtc,
    string EngineVersion,
    int MatchCount);

/// <summary>Dropdown contents for the create form.</summary>
public sealed record SubmissionFormOptionsDto(
    IReadOnlyList<CourseOptionDto> Courses,
    IReadOnlyList<SemesterOptionDto> Semesters);

public sealed record CourseOptionDto(int Id, string Code, string Title);

public sealed record SemesterOptionDto(int Id, string Name);
