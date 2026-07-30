using Microsoft.Extensions.Logging;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.Auditing;
using SmartPlagiarism.Core.Entities;
using SmartPlagiarism.Infrastructure.Data;

namespace SmartPlagiarism.Infrastructure.Services;

/// <inheritdoc cref="IAuditService"/>
public class AuditService : IAuditService
{
    private readonly AppDbContext _context;
    private readonly ILogger<AuditService> _logger;

    public AuditService(AppDbContext context, ILogger<AuditService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        var log = new AuditLog
        {
            UserId = entry.UserId,
            Action = entry.Action,
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            Details = entry.Details,
            TimestampUtc = DateTime.UtcNow,
        };

        _context.AuditLogs.Add(log);

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // An audit write must never take the user's action down with it - a
            // failed sign-out that leaves the user signed in would be worse than a
            // missing row. The failure is logged loudly instead.
            _context.AuditLogs.Remove(log);

            _logger.LogError(
                ex,
                "Failed to write audit entry {Action} for {EntityType} {EntityId}.",
                entry.Action,
                entry.EntityType,
                entry.EntityId);
        }
    }
}
