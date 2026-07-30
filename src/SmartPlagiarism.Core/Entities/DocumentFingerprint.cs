namespace SmartPlagiarism.Core.Entities;

/// <summary>
/// Winnowing fingerprints for one uploaded file, used to shortlist candidate
/// documents before expensive pairwise comparison.
///
/// The fingerprint set is stored as a single packed varbinary blob rather than as
/// one row per fingerprint. See <see cref="Fingerprints"/> for the rationale.
/// </summary>
public class DocumentFingerprint
{
    public int Id { get; set; }

    public int UploadedFileId { get; set; }

    public UploadedFile UploadedFile { get; set; } = null!;

    /// <summary>
    /// The fingerprint set packed as consecutive little-endian UInt64 values,
    /// sorted ascending so two sets can be intersected with a merge scan.
    ///
    /// Stored as a blob because the engine consumes the whole set at once: a child
    /// table would cost thousands of rows per document (millions across a corpus)
    /// and still be read in full for every comparison. A blob is one row and one
    /// sequential read. The trade-off is that shortlisting happens in memory rather
    /// than as a SQL GROUP BY over shared hashes; if the corpus ever outgrows that,
    /// an inverted-index table can be added alongside this column without changing
    /// the engine, which only ever sees an in-memory set.
    /// </summary>
    public byte[] Fingerprints { get; set; } = [];

    /// <summary>Number of fingerprints packed into <see cref="Fingerprints"/>.</summary>
    public int FingerprintCount { get; set; }

    /// <summary>
    /// Winnowing parameters this set was generated with. Fingerprints produced with
    /// different parameters are not comparable, so they are recorded per document
    /// rather than assumed constant.
    /// </summary>
    public int NGramSize { get; set; }

    /// <inheritdoc cref="NGramSize"/>
    public int WindowSize { get; set; }

    public DateTime GeneratedAtUtc { get; set; }
}
