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

    public DateTime AnalyzedAtUtc { get; set; }

    /// <summary>
    /// Engine build that produced this score, so an old report stays interpretable
    /// after the algorithm changes.
    /// </summary>
    public string EngineVersion { get; set; } = string.Empty;

    public ICollection<MatchedDocument> Matches { get; } = [];
}
