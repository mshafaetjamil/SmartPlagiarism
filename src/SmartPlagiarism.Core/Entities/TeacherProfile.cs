using SmartPlagiarism.Core.Identity;

namespace SmartPlagiarism.Core.Entities;

/// <summary>
/// Academic details for a user in the Teacher role, one-to-one with the Identity user.
/// </summary>
public class TeacherProfile
{
    public int Id { get; set; }

    /// <summary>Identity user this profile belongs to. Unique.</summary>
    public string UserId { get; set; } = string.Empty;

    public ApplicationUser User { get; set; } = null!;

    /// <summary>University-issued staff number, e.g. "T-1042".</summary>
    public string EmployeeNumber { get; set; } = string.Empty;

    public int DepartmentId { get; set; }

    public Department Department { get; set; } = null!;
}
