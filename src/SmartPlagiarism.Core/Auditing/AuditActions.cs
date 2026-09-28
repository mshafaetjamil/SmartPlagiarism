namespace SmartPlagiarism.Core.Auditing;

/// <summary>Values written to <c>AuditLog.Action</c>.</summary>
public static class AuditActions
{
    public const string Login = "Login";
    public const string LoginFailed = "LoginFailed";
    public const string Logout = "Logout";
    public const string AnalysisRequeued = "AnalysisRequeued";
    public const string SubmissionApproved = "SubmissionApproved";
    public const string SubmissionRejected = "SubmissionRejected";
    public const string UserCreated = "UserCreated";
    public const string UserDeactivated = "UserDeactivated";
    public const string UserReactivated = "UserReactivated";
    public const string RecordCreated = "RecordCreated";
    public const string RecordUpdated = "RecordUpdated";
    public const string RecordDeleted = "RecordDeleted";
    public const string ApiKeyCreated = "ApiKeyCreated";
    public const string ApiKeyRevoked = "ApiKeyRevoked";
    public const string ArchiveImportQueued = "ArchiveImportQueued";
}
