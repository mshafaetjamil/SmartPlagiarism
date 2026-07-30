using Microsoft.AspNetCore.Identity;

namespace SmartPlagiarism.Core.Identity;

/// <summary>
/// The application's Identity user. Kept in Core (not Infrastructure) so the
/// domain entities added in later phases - StudentProfile, TeacherProfile,
/// Submission - can declare real relationships to the user without Core taking
/// a dependency on the persistence layer.
/// </summary>
public class ApplicationUser : IdentityUser
{
    /// <summary>Display name shown in the UI and on generated reports.</summary>
    public string FullName { get; set; } = string.Empty;
}
