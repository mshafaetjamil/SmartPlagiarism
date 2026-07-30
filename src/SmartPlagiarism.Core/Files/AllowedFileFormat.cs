namespace SmartPlagiarism.Core.Files;

/// <summary>
/// An accepted upload format: its extension, the content type we record for it,
/// and the leading bytes a genuine file of that type must start with.
/// </summary>
public sealed class AllowedFileFormat
{
    internal AllowedFileFormat(string extension, string contentType, IReadOnlyList<byte[]> signatures)
    {
        Extension = extension;
        ContentType = contentType;
        Signatures = signatures;
    }

    /// <summary>Lowercase, including the leading dot.</summary>
    public string Extension { get; }

    /// <summary>
    /// The canonical content type. Recorded in place of the browser-supplied one,
    /// which is attacker-controlled and must never be trusted.
    /// </summary>
    public string ContentType { get; }

    /// <summary>Any one of these leading byte sequences marks a genuine file.</summary>
    public IReadOnlyList<byte[]> Signatures { get; }

    /// <summary>Longest signature, i.e. how many bytes need reading to decide.</summary>
    public int MaxSignatureLength => Signatures.Max(signature => signature.Length);
}
