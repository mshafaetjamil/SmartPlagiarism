namespace SmartPlagiarism.Core.Entities;

/// <summary>
/// One source a submission was found to overlap with, and by how much.
/// </summary>
public class MatchedDocument
{
    public int Id { get; set; }

    public int PlagiarismResultId { get; set; }

    public PlagiarismResult PlagiarismResult { get; set; } = null!;

    /// <summary>
    /// The file that was matched against. Null when the match came from a source
    /// outside the corpus - internet sources are not implemented yet, and this
    /// column is what will carry them without a schema change.
    /// </summary>
    public int? MatchedUploadedFileId { get; set; }

    public UploadedFile? MatchedUploadedFile { get; set; }

    /// <summary>Similarity against this one source, 0.00 to 100.00.</summary>
    public decimal SimilarityPercent { get; set; }

    /// <summary>
    /// The overlapping passages, as JSON, for the side-by-side match viewer.
    /// Stored as a document rather than as rows because it is always read and
    /// written whole, and is never queried by its contents.
    /// </summary>
    public string MatchedFragmentsJson { get; set; } = string.Empty;
}
