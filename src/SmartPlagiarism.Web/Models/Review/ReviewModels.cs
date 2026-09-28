using System.ComponentModel.DataAnnotations;
using System.Globalization;
using SmartPlagiarism.Core.DTOs;
using SmartPlagiarism.Core.DTOs.Review;
using SmartPlagiarism.Core.DTOs.Submissions;
using SmartPlagiarism.Core.Entities;
using SmartPlagiarism.Core.Enums;

namespace SmartPlagiarism.Web.Models.Review;

/// <summary>
/// The submission list's filters, sort and page, bound from the query string so
/// any view of the list can be bookmarked or linked to.
/// </summary>
public sealed class ReviewListFilter
{
    public int? CourseId { get; set; }

    public int? SemesterId { get; set; }

    public SubmissionStatus? Status { get; set; }

    public AnalysisStatus? Analysis { get; set; }

    [Range(0, 100, ErrorMessage = "Similarity must be between 0 and 100.")]
    [Display(Name = "Minimum similarity")]
    public decimal? MinSimilarity { get; set; }

    [Range(0, 100, ErrorMessage = "Similarity must be between 0 and 100.")]
    [Display(Name = "Maximum similarity")]
    public decimal? MaxSimilarity { get; set; }

    public ReviewSortField Sort { get; set; } = ReviewSortField.SubmittedAt;

    public bool Desc { get; set; } = true;

    public int Page { get; set; } = 1;

    public ReviewSubmissionQuery ToQuery() =>
        new(CourseId, SemesterId, Status, Analysis, MinSimilarity, MaxSimilarity, Sort, Desc, Page);

    /// <summary>
    /// This filter's query string with one thing changed, for sort headers and
    /// page links. Choosing a new sort goes back to page one.
    /// </summary>
    public Dictionary<string, string> RouteValues(int? page = null, ReviewSortField? sort = null)
    {
        var values = new Dictionary<string, string>();

        void Add(string key, object? value)
        {
            if (value is not null)
            {
                values[key] = Convert.ToString(value, CultureInfo.InvariantCulture)!;
            }
        }

        Add(nameof(CourseId), CourseId);
        Add(nameof(SemesterId), SemesterId);
        Add(nameof(Status), Status);
        Add(nameof(Analysis), Analysis);
        Add(nameof(MinSimilarity), MinSimilarity);
        Add(nameof(MaxSimilarity), MaxSimilarity);

        if (sort is { } newSort)
        {
            // Clicking the current column flips its direction; a new column starts
            // with the order a teacher most likely wants - highest or newest first.
            Add(nameof(Sort), newSort);
            Add(nameof(Desc), newSort == Sort ? !Desc : newSort is ReviewSortField.Similarity or ReviewSortField.SubmittedAt);
            Add(nameof(Page), 1);
        }
        else
        {
            Add(nameof(Sort), Sort);
            Add(nameof(Desc), Desc);
            Add(nameof(Page), page ?? Page);
        }

        return values;
    }
}

public sealed record ReviewListViewModel(
    ReviewListFilter Filter,
    PagedResult<ReviewSubmissionListItemDto> Results,
    IReadOnlyList<CourseOptionDto> Courses,
    IReadOnlyList<SemesterOptionDto> Semesters);

/// <summary>The comment posted with an approval or a rejection.</summary>
public sealed class ReviewDecisionForm
{
    [StringLength(Submission.MaxTeacherCommentLength)]
    public string? Comment { get; set; }
}

/// <summary>
/// Names the similarity measures stored with a match, in the order they are
/// shown. The keys are the algorithm names the engine records.
/// </summary>
public static class SupportingScoreLabels
{
    private static readonly (string Key, string Label)[] s_measures =
    [
        ("combined", "weighted blend"),
        ("ngram-3", "three-word sequences"),
        ("tfidf-cosine", "vocabulary (TF-IDF cosine)"),
        ("jaccard", "shared vocabulary (Jaccard)"),
    ];

    public static string Describe(IReadOnlyDictionary<string, double> scores) =>
        string.Join(
            " \u00B7 ",
            s_measures
                .Where(measure => scores.ContainsKey(measure.Key))
                .Select(measure => string.Create(CultureInfo.InvariantCulture, $"{measure.Label} {scores[measure.Key] * 100:0}%")));
}
