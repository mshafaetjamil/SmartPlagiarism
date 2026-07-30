namespace SmartPlagiarism.Core.Auditing;

/// <summary>Values written to <c>AuditLog.Action</c>.</summary>
public static class AuditActions
{
    public const string Login = "Login";
    public const string LoginFailed = "LoginFailed";
    public const string Logout = "Logout";
}
