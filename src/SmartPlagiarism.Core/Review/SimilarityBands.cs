namespace SmartPlagiarism.Core.Review;

/// <summary>How worrying an overall similarity is, for colour-coding.</summary>
public enum SimilarityBand
{
    /// <summary>Below 20%: the background of quotations and common phrasing.</summary>
    Low,

    /// <summary>20% to 50%: worth a teacher's look.</summary>
    Moderate,

    /// <summary>Above 50%: most of the work was found elsewhere.</summary>
    High,
}

/// <summary>
/// The thresholds, in one place so the web pages and the PDF reports can never
/// disagree about which colour a score is.
/// </summary>
public static class SimilarityBands
{
    /// <summary>Scores from here upward are at least <see cref="SimilarityBand.Moderate"/>.</summary>
    public const decimal ModerateFrom = 20m;

    /// <summary>Scores above this are <see cref="SimilarityBand.High"/>; 50% itself is still moderate.</summary>
    public const decimal HighAbove = 50m;

    public static SimilarityBand Classify(decimal percent) => percent switch
    {
        < ModerateFrom => SimilarityBand.Low,
        <= HighAbove => SimilarityBand.Moderate,
        _ => SimilarityBand.High,
    };
}
