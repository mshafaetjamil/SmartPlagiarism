namespace SmartPlagiarism.Core.Identity;

/// <summary>
/// The three roles this system recognises. Lives in Core so that both the
/// Infrastructure seeder and the Web authorization attributes bind to the same
/// constants instead of repeating magic strings.
/// </summary>
public static class ApplicationRoles
{
    public const string Admin = "Admin";
    public const string Teacher = "Teacher";
    public const string Student = "Student";

    /// <summary>All roles, in descending order of privilege.</summary>
    public static IReadOnlyList<string> All { get; } = [Admin, Teacher, Student];
}
