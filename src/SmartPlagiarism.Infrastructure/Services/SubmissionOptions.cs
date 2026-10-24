using System.ComponentModel.DataAnnotations;

namespace SmartPlagiarism.Infrastructure.Services;

/// <summary>
/// How much one student may submit, bound from "Submissions". Without a limit a
/// single account could upload five 25 MB files again and again - filling the
/// disk, and keeping the analysis workers busy while classmates' work waits.
/// Checked by <see cref="SubmissionService"/>, so the web form and the REST API
/// are held to the same limits.
/// </summary>
public class SubmissionOptions
{
    public const string SectionName = "Submissions";

    /// <summary>Submissions one student may have waiting for or under analysis at once.</summary>
    [Range(1, 100)]
    public int MaxPendingPerStudent { get; set; } = 3;

    /// <summary>Submissions one student may make in any 24 hours.</summary>
    [Range(1, 1000)]
    public int MaxPerStudentPerDay { get; set; } = 10;
}
