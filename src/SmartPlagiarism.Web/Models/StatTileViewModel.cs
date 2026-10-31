namespace SmartPlagiarism.Web.Models;

/// <summary>A headline figure on a dashboard: Views/Shared/_StatTile.</summary>
/// <param name="ValueClass">A colour for the figure, when it means something.</param>
public sealed record StatTileViewModel(string Label, string Value, string? Note = null, string? ValueClass = null)
{
    /// <summary>An average score, coloured by its band; a dash when nothing has been analysed.</summary>
    public static StatTileViewModel Similarity(string label, decimal? percent) => percent is { } value
        ? new(label, StatusBadge.FormatPercent(value), ValueClass: StatusBadge.SimilarityTextClass(value))
        : new(label, "-");
}
