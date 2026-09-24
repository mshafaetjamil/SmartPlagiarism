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

    /// <summary>
    /// Resource-based: may this user see this submission - its details, its
    /// analysis, its report? Staff may see any; a student only their own.
    /// </summary>
    public const string CanViewSubmission = "CanViewSubmission";

    /// <summary>A REST API call authenticated by an API key rather than a sign-in cookie.</summary>
    public const string ApiClient = "ApiClient";
}
