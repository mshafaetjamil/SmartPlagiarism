using System.Text.Json;

namespace SmartPlagiarism.Engine.Similarity;

/// <summary>
/// Reads and writes the JSON stored in <c>MatchedDocument.ComponentScoresJson</c>:
/// each algorithm's ratio for one match, keyed by algorithm name.
/// </summary>
public static class ComponentScoreSerializer
{
    public static string Serialize(IReadOnlyDictionary<string, double> scores)
    {
        ArgumentNullException.ThrowIfNull(scores);
        return JsonSerializer.Serialize(scores.ToDictionary(entry => entry.Key, entry => Math.Round(entry.Value, 4)));
    }

    /// <returns>The scores, or none for matches stored before they were kept.</returns>
    public static IReadOnlyDictionary<string, double> Deserialize(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? new Dictionary<string, double>()
            : JsonSerializer.Deserialize<Dictionary<string, double>>(json) ?? [];
}
