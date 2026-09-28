namespace SmartPlagiarism.Core.Entities;

/// <summary>
/// The outcome of one analysis run. One current result per submission - a re-run
/// replaces it, since the UI and reports only ever show the latest verdict.
/// </summary>
public class PlagiarismResult
{
    public int Id { get; set; }

    public int SubmissionId { get; set; }

    public Submission Submission { get; set; } = null!;

    /// <summary>Overall similarity as a percentage, 0.00 to 100.00.</summary>
    public decimal OverallSimilarityPercent { get; set; }

    /// <summary>
    /// Share of the submission, 0-100, in passages common to most of its course -
    /// a cover page, a declaration - which is set aside rather than counted in
    /// <see cref="OverallSimilarityPercent"/>. Shown so nothing is hidden silently.
    /// </summary>
    public decimal CommonTextPercent { get; set; }

    public DateTime AnalyzedAtUtc { get; set; }

    /// <summary>
    /// Engine build that produced this score, so an old report stays interpretable
    /// after the algorithm changes.
    /// </summary>
    public string EngineVersion { get; set; } = string.Empty;

    public ICollection<MatchedDocument> Matches { get; } = [];
}
