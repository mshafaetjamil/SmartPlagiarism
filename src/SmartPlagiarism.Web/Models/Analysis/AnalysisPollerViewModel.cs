using SmartPlagiarism.Core.Enums;

namespace SmartPlagiarism.Web.Models.Analysis;

/// <summary>
/// What a page hands the status poller: the endpoint to ask, and the status it
/// rendered for each submission still in flight. The poller reloads the page as
/// soon as any of those changes.
/// </summary>
public sealed class AnalysisPollerViewModel
{
    private AnalysisPollerViewModel(string statusUrl, IReadOnlyDictionary<string, string> pending)
    {
        StatusUrl = statusUrl;
        Pending = pending;
    }

    /// <summary>The status endpoint, with every pending id in its query string.</summary>
    public string StatusUrl { get; }

    /// <summary>Submission id to the status the page showed, for each one still in flight.</summary>
    public IReadOnlyDictionary<string, string> Pending { get; }

    public bool HasPending => Pending.Count > 0;

    /// <param name="statusEndpoint">The endpoint's path, without a query string.</param>
    public static AnalysisPollerViewModel For(
        string statusEndpoint,
        IEnumerable<(int Id, AnalysisStatus Status)> submissions)
    {
        var pending = submissions
            .Where(submission => submission.Status is AnalysisStatus.Queued or AnalysisStatus.Processing)
            .DistinctBy(submission => submission.Id)
            .ToDictionary(
                submission => submission.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                submission => submission.Status.ToString());

        var query = string.Join("&", pending.Keys.Select(id => $"ids={id}"));

        return new AnalysisPollerViewModel(pending.Count > 0 ? $"{statusEndpoint}?{query}" : statusEndpoint, pending);
    }
}
