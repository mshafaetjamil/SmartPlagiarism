using System.Globalization;
using System.IO.Compression;
using DocumentFormat.OpenXml.Packaging;

namespace SmartPlagiarism.Engine.Extraction;

/// <summary>
/// Opens DOCX and PPTX packages only once they are known to be of a size worth
/// reading. Both are ZIP files, and a ZIP of a few megabytes can unpack to
/// gigabytes; the Open XML SDK would then build all of it in memory.
/// </summary>
internal static class OfficePackage
{
    /// <summary>
    /// Checks the package's ZIP directory against the limits and returns the
    /// settings to open it with, which bound each part again as it is read.
    /// </summary>
    /// <exception cref="DocumentTooLargeException">The package is over a limit.</exception>
    public static OpenSettings CheckedOpenSettings(byte[] package, ExtractionLimits limits)
    {
        using (var zip = new ZipArchive(new MemoryStream(package, writable: false), ZipArchiveMode.Read))
        {
            if (zip.Entries.Count > limits.MaxOfficePackageEntries)
            {
                throw new DocumentTooLargeException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The file holds {zip.Entries.Count:N0} internal parts, more than the {limits.MaxOfficePackageEntries:N0} allowed."));
            }

            var unpacked = zip.Entries.Sum(entry => entry.Length);

            if (unpacked > limits.MaxUnpackedOfficeBytes)
            {
                throw new DocumentTooLargeException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The file unpacks to {unpacked / (1024 * 1024):N0} MB, more than the {limits.MaxUnpackedOfficeBytes / (1024 * 1024):N0} MB allowed."));
            }
        }

        return new OpenSettings { MaxCharactersInPart = limits.MaxCharactersInOfficePart };
    }
}
