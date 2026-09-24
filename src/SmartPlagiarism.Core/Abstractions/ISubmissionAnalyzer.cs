namespace SmartPlagiarism.Core.Abstractions;

/// <summary>
/// Runs the analysis of one submission end to end: extract each file's text,
/// store its fingerprints, compare it against the corpus, and store the result.
///
/// Used only by the background worker, one submission at a time and each in its
/// own DI scope. Web requests go through <see cref="IAnalysisService"/> instead.
/// </summary>
public interface ISubmissionAnalyzer
{
    /// <summary>
    /// Analyses a queued submission and leaves it Completed or Failed. Does
    /// nothing when the submission is missing or no longer Queued, so a duplicate
    /// queue entry is harmless. Failures are recorded on the submission, not
    /// thrown; only cancellation propagates.
    /// </summary>
    Task AnalyzeAsync(int submissionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns submissions left Processing by a previous run to the queue, and
    /// lists every queued submission, oldest first, for the worker to analyse at
    /// startup before anything newer.
    /// </summary>
    Task<IReadOnlyList<int>> ResumePendingAsync(CancellationToken cancellationToken = default);
}
