namespace SmartPlagiarism.Core.Auditing;

/// <summary>
/// One thing worth recording in the audit trail.
/// </summary>
/// <param name="Action">What happened - see <see cref="AuditActions"/>.</param>
/// <param name="EntityType">Type of the affected entity - see <see cref="AuditEntityTypes"/>.</param>
/// <param name="EntityId">Key of the affected entity, or null if the action targets nothing specific.</param>
/// <param name="UserId">Who did it, or null for actions taken by the system.</param>
/// <param name="Details">Free-form context, e.g. the caller's IP address.</param>
public sealed record AuditEntry(
    string Action,
    string EntityType,
    string? EntityId = null,
    string? UserId = null,
    string? Details = null);
