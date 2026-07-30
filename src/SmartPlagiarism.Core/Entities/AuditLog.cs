namespace SmartPlagiarism.Core.Entities;

/// <summary>
/// An append-only record of a security- or data-relevant action.
///
/// <see cref="UserId"/> is deliberately a plain column and not a foreign key to
/// the Identity user: an audit trail has to outlive the account it describes, and
/// a foreign key would either block the deletion or take the history with it.
/// </summary>
public class AuditLog
{
    public long Id { get; set; }

    /// <summary>Identity user id, or null for actions taken by the system itself.</summary>
    public string? UserId { get; set; }

    /// <summary>What happened, e.g. "Login", "SubmissionApproved".</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Type of the affected entity, e.g. "Submission".</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Key of the affected entity, as text so that both int and string keys fit.
    /// Null for actions that target no particular entity.
    /// </summary>
    public string? EntityId { get; set; }

    public DateTime TimestampUtc { get; set; }

    /// <summary>Free-form context, e.g. the IP address or what changed.</summary>
    public string? Details { get; set; }
}
