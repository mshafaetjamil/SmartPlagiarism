namespace SmartPlagiarism.Engine.Similarity;

/// <summary>
/// Scores how similar two documents are.
///
/// Implementations return a ratio in <c>[0, 1]</c>, not a percentage: the maths
/// is cleaner and conversion happens once, at the report boundary. They must
/// return 0 rather than NaN when either document is empty.
///
/// The first document is the source - an existing document - and the second the
/// one being analysed. Vocabulary measures are symmetric and ignore the order;
/// n-gram containment is not, because it asks how much of the analysed document
/// appears in the source.
/// </summary>
public interface ISimilarityAlgorithm
{
    /// <summary>Short identifier, recorded alongside scores so a result can be explained.</summary>
    string Name { get; }

    /// <param name="documentA">The source: an existing document.</param>
    /// <param name="documentB">The document being analysed.</param>
    /// <returns>Similarity in <c>[0, 1]</c>.</returns>
    double Compute(AnalyzedDocument documentA, AnalyzedDocument documentB);
}
