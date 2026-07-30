namespace SmartPlagiarism.Core.Entities;

/// <summary>
/// An academic term. Submissions are scoped to one, so a course's work can be
/// compared within a term or across the full history.
/// </summary>
public class Semester
{
    public int Id { get; set; }

    /// <summary>Display name, e.g. "Summer 2026".</summary>
    public string Name { get; set; } = string.Empty;

    public DateTime StartDateUtc { get; set; }

    public DateTime EndDateUtc { get; set; }

    /// <summary>True when <paramref name="utcNow"/> falls inside this semester's date range.</summary>
    public bool IsActiveOn(DateTime utcNow) => utcNow >= StartDateUtc && utcNow <= EndDateUtc;
}
