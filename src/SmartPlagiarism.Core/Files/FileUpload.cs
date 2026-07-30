namespace SmartPlagiarism.Core.Files;

/// <summary>
/// One file being uploaded, in a form Core can accept without knowing about
/// ASP.NET's IFormFile. The Web layer maps to this; it does not own the stream's
/// lifetime, so the caller disposes it.
/// </summary>
/// <param name="FileName">Client-supplied filename. Untrusted.</param>
/// <param name="SizeBytes">Length reported by the transport.</param>
/// <param name="Content">Seekable stream over the file's bytes.</param>
public sealed record FileUpload(string FileName, long SizeBytes, Stream Content);
