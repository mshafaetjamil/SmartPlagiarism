using SmartPlagiarism.Core.Auditing;

namespace SmartPlagiarism.Core.Abstractions;

/// <summary>
/// Appends entries to the audit trail. Implementations must never throw for a
/// failed write: losing an audit row is bad, but failing the user's action
/// because of it is worse.
/// </summary>
public interface IAuditService
{
    Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}
