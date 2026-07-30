using System.ComponentModel.DataAnnotations;

namespace SmartPlagiarism.Infrastructure.Storage;

public class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>
    /// Where uploaded documents live. May be absolute, or relative to the content
    /// root - it is resolved to an absolute path at startup. Must be outside
    /// wwwroot so uploads are never served as static files.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string RootPath { get; set; } = string.Empty;
}
