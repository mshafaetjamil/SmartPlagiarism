namespace SmartPlagiarism.Core.Entities;

/// <summary>
/// Winnowing fingerprints for one uploaded file, used to shortlist candidate
/// documents before expensive pairwise comparison.
///
/// The set is kept twice: packed in one row with the document, and as rows of an
/// inverted index (<see cref="FingerprintHash"/>) for finding documents by hash.
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
    /// The document's own copy of its set, in one row. Shortlisting does not read
    /// it: that goes through <see cref="Hashes"/>, the inverted index, so that it
    /// costs a seek per shared hash rather than a read of every document.
    /// </summary>
    public byte[] Fingerprints { get; set; } = [];

    /// <summary>The same fingerprints as rows of the inverted index.</summary>
    public ICollection<FingerprintHash> Hashes { get; } = [];

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
