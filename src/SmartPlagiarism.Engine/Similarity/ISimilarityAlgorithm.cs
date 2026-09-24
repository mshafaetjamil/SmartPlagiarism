namespace SmartPlagiarism.Engine.Similarity;

/// <summary>
/// Scores how similar two documents are.
///
/// Implementations return a ratio in <c>[0, 1]</c>, not a percentage: the maths
/// is cleaner and conversion happens once, at the report boundary. They must be
/// symmetric - <c>Compute(a, b)</c> equals <c>Compute(b, a)</c> - and must return
/// 0 rather than NaN when either document is empty.
/// </summary>
public interface ISimilarityAlgorithm
{
    /// <summary>Short identifier, recorded alongside scores so a result can be explained.</summary>
    string Name { get; }

    /// <returns>Similarity in <c>[0, 1]</c>.</returns>
    double Compute(AnalyzedDocument documentA, AnalyzedDocument documentB);
}
