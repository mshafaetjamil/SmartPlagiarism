namespace SmartPlagiarism.Engine;

/// <summary>
/// Identifies the plagiarism engine build that produced a result. Stored against
/// every analysis so historical reports stay interpretable after the engine's
/// algorithms or thresholds change.
/// </summary>
public static class EngineInfo
{
    /// <summary>
    /// Engine version recorded on each analysis. Bump this whenever a change
    /// alters the similarity scores the engine produces.
    /// </summary>
    public const string Version = "0.1.0";
}
