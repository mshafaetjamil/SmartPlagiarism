namespace SmartPlagiarism.Core.Entities;

/// <summary>
/// One winnowing fingerprint of one document: a row of the inverted index that
/// finds, for a new submission's fingerprints, the documents sharing them.
///
/// The shortlist is then a single seek-and-count in the database - "which
/// documents hold these hashes, and how many of them" - instead of reading every
/// document's fingerprints into memory for every analysis, which grew with the
/// corpus.
/// </summary>
public class FingerprintHash
{
    /// <summary>The 64-bit fingerprint, stored as the signed value with the same bits.</summary>
    public long Hash { get; set; }

    public int DocumentFingerprintId { get; set; }

    public DocumentFingerprint DocumentFingerprint { get; set; } = null!;
}
