namespace SmartPlagiarism.Core.Files;

/// <summary>A file that has been hashed and written to the content-addressed store.</summary>
/// <param name="Sha256Hash">Lowercase hex digest of the contents.</param>
/// <param name="StoragePath">
/// Path relative to the storage root, always forward-slashed, e.g.
/// <c>ab/abcd...ef.pdf</c>. Relative so the root can move without a data migration.
/// </param>
/// <param name="ContentType">Canonical content type for the detected format.</param>
/// <param name="SizeBytes">Size of the stored file.</param>
/// <param name="WasDeduplicated">
/// True when identical bytes were already stored and were reused instead of written again.
/// </param>
public sealed record StoredFile(
    string Sha256Hash,
    string StoragePath,
    string ContentType,
    long SizeBytes,
    bool WasDeduplicated);
