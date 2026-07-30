using SmartPlagiarism.Core.Identity;

namespace SmartPlagiarism.Core.Entities;

/// <summary>
/// Academic details for a user in the Student role, one-to-one with the Identity
/// user. Kept separate from <see cref="ApplicationUser"/> so authentication data
/// and academic data evolve independently.
/// </summary>
public class StudentProfile
{
    public int Id { get; set; }

    /// <summary>Identity user this profile belongs to. Unique.</summary>
    public string UserId { get; set; } = string.Empty;

    public ApplicationUser User { get; set; } = null!;

    /// <summary>
    /// The university-issued student number, e.g. "2026-CSE-001". Named
    /// StudentNumber rather than StudentId so it is never confused with the
    /// surrogate key or with <see cref="Submission.StudentProfileId"/>.
    /// </summary>
    public string StudentNumber { get; set; } = string.Empty;

    public int DepartmentId { get; set; }

    public Department Department { get; set; } = null!;

    public int EnrollmentYear { get; set; }

    public ICollection<Submission> Submissions { get; } = [];
}
