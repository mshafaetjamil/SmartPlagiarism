namespace SmartPlagiarism.Engine.Extraction;

internal static class StreamHelpers
{
    /// <summary>
    /// Buffers a document into memory. PdfPig and the Open XML SDK both need
    /// random access, and the source is often a forward-only file or network
    /// stream. This is the one genuinely asynchronous part of extraction - the
    /// parsing that follows is CPU-bound.
    /// </summary>
    internal static async Task<byte[]> ReadAllBytesAsync(Stream source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source is MemoryStream alreadyBuffered)
        {
            return alreadyBuffered.ToArray();
        }

        using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer, cancellationToken);

        return buffer.ToArray();
    }
}
