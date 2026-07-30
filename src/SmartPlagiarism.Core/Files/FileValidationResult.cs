namespace SmartPlagiarism.Core.Files;

/// <summary>Outcome of validating one uploaded file.</summary>
public sealed class FileValidationResult
{
    private FileValidationResult(bool isValid, string? errorMessage, AllowedFileFormat? format, string? safeFileName)
    {
        IsValid = isValid;
        ErrorMessage = errorMessage;
        Format = format;
        SafeFileName = safeFileName;
    }

    public bool IsValid { get; }

    /// <summary>Why it was rejected. Null when <see cref="IsValid"/>.</summary>
    public string? ErrorMessage { get; }

    /// <summary>The matched format. Null unless <see cref="IsValid"/>.</summary>
    public AllowedFileFormat? Format { get; }

    /// <summary>
    /// The client filename with any directory component stripped. Null unless
    /// <see cref="IsValid"/>. Safe to store and display, never to build a path from.
    /// </summary>
    public string? SafeFileName { get; }

    public static FileValidationResult Valid(AllowedFileFormat format, string safeFileName) =>
        new(true, null, format, safeFileName);

    public static FileValidationResult Invalid(string errorMessage) =>
        new(false, errorMessage, null, null);
}
