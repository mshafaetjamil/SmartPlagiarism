using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartPlagiarism.Engine.Matching;

/// <summary>
/// One passage that appears in both documents, as character offsets into each.
///
/// "Source" is the existing document that was matched against; "target" is the
/// submission being analysed. The match viewer shows the submission on the left
/// and the source on the right, so both spans are needed to highlight the pair.
///
/// Offsets index the extracted text exactly as stored, so a viewer can substring
/// straight into it with no further mapping.
/// </summary>
/// <param name="SourceStart">Inclusive start offset in the matched document.</param>
/// <param name="SourceEnd">Exclusive end offset in the matched document.</param>
/// <param name="TargetStart">Inclusive start offset in the analysed document.</param>
/// <param name="TargetEnd">Exclusive end offset in the analysed document.</param>
/// <param name="Similarity">
/// Proportion of this span that matched exactly, in <c>[0, 1]</c>. Below 1 when
/// small gaps were bridged to keep one passage as a single fragment.
/// </param>
public sealed record MatchedFragment(
    [property: JsonPropertyName("sourceStart")] int SourceStart,
    [property: JsonPropertyName("sourceEnd")] int SourceEnd,
    [property: JsonPropertyName("targetStart")] int TargetStart,
    [property: JsonPropertyName("targetEnd")] int TargetEnd,
    [property: JsonPropertyName("similarity")] double Similarity)
{
    [JsonIgnore]
    public int SourceLength => SourceEnd - SourceStart;

    [JsonIgnore]
    public int TargetLength => TargetEnd - TargetStart;
}

/// <summary>
/// Reads and writes the JSON stored in <c>MatchedDocument.MatchedFragmentsJson</c>.
/// </summary>
public static class MatchedFragmentSerializer
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        // Compact: this column holds one entry per matched passage and is written
        // for every match found.
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string Serialize(IReadOnlyList<MatchedFragment> fragments)
    {
        ArgumentNullException.ThrowIfNull(fragments);

        return JsonSerializer.Serialize(fragments, s_options);
    }

    public static IReadOnlyList<MatchedFragment> Deserialize(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<MatchedFragment>>(json, s_options) ?? [];
}
