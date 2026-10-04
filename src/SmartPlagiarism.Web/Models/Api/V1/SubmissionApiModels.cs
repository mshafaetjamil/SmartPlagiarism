using System.ComponentModel.DataAnnotations;
using SmartPlagiarism.Core.DTOs.Submissions;
using SmartPlagiarism.Core.Entities;
using SmartPlagiarism.Core.Enums;
using SmartPlagiarism.Core.Review;

namespace SmartPlagiarism.Web.Models.Api.V1;

/// <summary>The multipart form a portal posts to create a submission.</summary>
public sealed class CreateSubmissionApiRequest
{
    /// <summary>The student the work belongs to, by their university number.</summary>
    [Required]
    [StringLength(StudentProfile.MaxNumberLength)]
    public string StudentNumber { get; set; } = string.Empty;

    [Required]
    [StringLength(Submission.MaxTitleLength, MinimumLength = Submission.MinTitleLength)]
    public string Title { get; set; } = string.Empty;

    public SubmissionType Type { get; set; } = SubmissionType.Report;

    [Range(1, int.MaxValue)]
    public int CourseId { get; set; }

    [Range(1, int.MaxValue)]
    public int SemesterId { get; set; }

    /// <summary>One to five PDF, DOCX or PPTX files, each at most 25 MB.</summary>
    [Required]
    public List<IFormFile> Files { get; set; } = [];
}

/// <summary>A submission's status and, once analysed, its result.</summary>
/// <param name="Result">Null until the analysis has completed.</param>
/// <param name="AnalysisError">Why the analysis failed, when it did.</param>
public sealed record SubmissionApiResponse(
    int Id,
    string Title,
    SubmissionType Type,
    DateTime SubmittedAtUtc,
    AnalysisStatus AnalysisStatus,
    string? AnalysisError,
    SubmissionStatus ReviewStatus,
    string? TeacherComment,
    SubmissionApiResult? Result,
    SubmissionApiLinks Links)
{
    public static SubmissionApiResponse From(SubmissionDetailDto detail, SubmissionApiLinks links) => new(
        detail.Id,
        detail.Title,
        detail.Type,
        detail.SubmittedAtUtc,
        detail.AnalysisStatus,
        detail.AnalysisError,
        detail.Status,
        detail.TeacherComment,
        detail.Result is { } result
            ? new SubmissionApiResult(
                result.OverallSimilarityPercent,
                SimilarityBands.Classify(result.OverallSimilarityPercent),
                result.CommonTextPercent,
                result.AnalyzedAtUtc,
                result.EngineVersion,
                result.MatchCount)
            : null,
        links);
}

/// <param name="SimilarityBand">Low below 20%, Moderate to 50%, High above - as on the web pages.</param>
/// <param name="CommonTextPercent">
/// Share set aside as text most of the course shares (a cover page, a declaration), not counted.
/// </param>
public sealed record SubmissionApiResult(
    decimal OverallSimilarityPercent,
    SimilarityBand SimilarityBand,
    decimal CommonTextPercent,
    DateTime AnalyzedAtUtc,
    string EngineVersion,
    int MatchedSources);

public sealed record SubmissionApiLinks(string Self, string Report);

/// <summary>Settings for the REST API.</summary>
public sealed class ApiOptions
{
    public const string SectionName = "Api";

    /// <summary>Uploads each API key may make per minute before getting 429.</summary>
    [Range(1, 10_000)]
    public int UploadsPerMinute { get; set; } = 10;
}

/// <summary>Names of the rate-limiting policies.</summary>
public static class ApiRateLimits
{
    public const string Uploads = "api-uploads";
}
