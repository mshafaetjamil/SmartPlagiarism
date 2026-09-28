using System.Globalization;
using System.Text;

namespace SmartPlagiarism.Infrastructure.Archive;

/// <summary>
/// The optional <c>manifest.csv</c> in an archive ZIP: one row per file, giving
/// what the file itself cannot - its title, author, year, department and course.
///
/// Columns are found by their header (<c>file</c>, <c>title</c>, <c>author</c>,
/// <c>year</c>, <c>department</c>, <c>course</c>, in any order; only <c>file</c>
/// is required), so a spreadsheet exported with extra columns still works. A
/// row's <c>file</c> is its path inside the ZIP relative to the manifest, or just
/// its file name when that is unique. Standard CSV: fields may be quoted, and a
/// quoted field may hold commas, quotes (doubled) and line breaks.
/// </summary>
public sealed class ArchiveManifest
{
    public sealed record Row(string? Title, string? Author, int? Year, string? DepartmentCode, string? CourseCode);

    private readonly Dictionary<string, Row> _byPath = new(StringComparer.OrdinalIgnoreCase);

    // Null marks a file name that several rows share, so it cannot be matched by name alone.
    private readonly Dictionary<string, Row?> _byName = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<string> _problems = [];

    private ArchiveManifest()
    {
    }

    public static ArchiveManifest Empty { get; } = new();

    /// <summary>Rows that could not be used, and why - reported with the import.</summary>
    public IReadOnlyList<string> Problems => _problems;

    public int RowCount => _byPath.Count;

    public static ArchiveManifest Parse(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var manifest = new ArchiveManifest();
        var records = ReadRecords(reader).ToList();

        if (records.Count == 0)
        {
            manifest._problems.Add("manifest.csv is empty.");
            return manifest;
        }

        var header = records[0].Select(name => name.Trim().ToLowerInvariant()).ToList();
        var fileColumn = header.IndexOf("file");

        if (fileColumn < 0)
        {
            manifest._problems.Add("manifest.csv has no \"file\" column, so it was ignored.");
            return manifest;
        }

        string? Cell(IReadOnlyList<string> record, string column)
        {
            var index = header.IndexOf(column);
            var value = index >= 0 && index < record.Count ? record[index].Trim() : string.Empty;
            return value.Length == 0 ? null : value;
        }

        for (var line = 1; line < records.Count; line++)
        {
            var record = records[line];
            var file = Cell(record, "file");

            if (file is null)
            {
                // A blank line, or a row with no file: nothing to attach it to.
                continue;
            }

            int? year = null;

            if (Cell(record, "year") is { } yearText)
            {
                if (int.TryParse(yearText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                {
                    year = parsed;
                }
                else
                {
                    manifest._problems.Add($"manifest.csv row {line + 1}: \"{yearText}\" is not a year; the file's folder or the default year is used instead.");
                }
            }

            manifest.Add(
                NormalizePath(file),
                new Row(Cell(record, "title"), Cell(record, "author"), year, Cell(record, "department"), Cell(record, "course")));
        }

        return manifest;
    }

    /// <summary>The row for a file, by its path relative to the manifest or else by its unique file name.</summary>
    public Row? Find(string relativePath)
    {
        var path = NormalizePath(relativePath);

        if (_byPath.TryGetValue(path, out var row))
        {
            return row;
        }

        return _byName.TryGetValue(Path.GetFileName(path), out var byName) ? byName : null;
    }

    /// <summary>Forward slashes, no leading "./" or "/", so paths from Windows and Mac zips compare equal.</summary>
    public static string NormalizePath(string path)
    {
        var normalized = path.Replace('\\', '/').Trim();

        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized.TrimStart('/');
    }

    private void Add(string path, Row row)
    {
        _byPath[path] = row;

        var name = Path.GetFileName(path);
        _byName[name] = _byName.ContainsKey(name) ? null : row;
    }

    /// <summary>RFC 4180 records: quoted fields, doubled quotes, line breaks inside quotes.</summary>
    private static IEnumerable<List<string>> ReadRecords(TextReader reader)
    {
        var record = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var fieldStarted = false;
        int current;

        while ((current = reader.Read()) >= 0)
        {
            var character = (char)current;

            if (quoted)
            {
                if (character != '"')
                {
                    field.Append(character);
                }
                else if (reader.Peek() == '"')
                {
                    field.Append('"');
                    reader.Read();
                }
                else
                {
                    quoted = false;
                }

                continue;
            }

            switch (character)
            {
                case '"' when !fieldStarted:
                    quoted = true;
                    fieldStarted = true;
                    break;
                case ',':
                    record.Add(field.ToString());
                    field.Clear();
                    fieldStarted = false;
                    break;
                case '\r':
                    break;
                case '\n':
                    record.Add(field.ToString());
                    field.Clear();
                    fieldStarted = false;
                    yield return record;
                    record = [];
                    break;
                default:
                    field.Append(character);
                    fieldStarted = true;
                    break;
            }
        }

        if (fieldStarted || field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            yield return record;
        }
    }
}
