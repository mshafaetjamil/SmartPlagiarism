namespace SmartPlagiarism.Core.Identity;

/// <summary>
/// Names of the role-based authorization policies. Lives beside
/// <see cref="ApplicationRoles"/> so controllers reference a constant rather
/// than repeating a string that no compiler would check.
/// </summary>
public static class AuthorizationPolicies
{
    public const string AdminOnly = "AdminOnly";
    public const string TeacherOnly = "TeacherOnly";
    public const string StudentOnly = "StudentOnly";
}
