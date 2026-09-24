using Microsoft.EntityFrameworkCore;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.DTOs;
using SmartPlagiarism.Core.DTOs.Admin;
using SmartPlagiarism.Infrastructure.Data;

namespace SmartPlagiarism.Infrastructure.Admin;

/// <inheritdoc cref="IAuditLogReader"/>
public class AuditLogReader : IAuditLogReader
{
    private readonly AppDbContext _context;

    public AuditLogReader(AppDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<AuditLogEntryDto>> GetEntriesAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // AuditLog.UserId is deliberately not a foreign key - the log outlives the
        // users in it - so the email comes from a left join, and is null for
        // system actions and deleted accounts.
        var entries =
            from log in _context.AuditLogs.AsNoTracking()
            join user in _context.Users.AsNoTracking() on log.UserId equals user.Id into users
            from user in users.DefaultIfEmpty()
            select new { Log = log, Email = user == null ? null : user.Email };

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            entries = entries.Where(entry => entry.Log.Action == query.Action);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            entries = entries.Where(entry => entry.Log.EntityType == query.EntityType);
        }

        if (!string.IsNullOrWhiteSpace(query.User))
        {
            var term = query.User.Trim();
            entries = entries.Where(entry => entry.Email != null && entry.Email.Contains(term));
        }

        if (query.FromDate is { } fromDate)
        {
            var from = fromDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            entries = entries.Where(entry => entry.Log.TimestampUtc >= from);
        }

        if (query.ToDate is { } toDate)
        {
            // Whole days: "to the 5th" includes everything on the 5th.
            var before = toDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            entries = entries.Where(entry => entry.Log.TimestampUtc < before);
        }

        var totalCount = await entries.CountAsync(cancellationToken);
        var page = PagedResult<AuditLogEntryDto>.ClampPage(query.Page, totalCount, AuditLogQuery.PageSize);

        var items = await entries
            .OrderByDescending(entry => entry.Log.TimestampUtc)
            .ThenByDescending(entry => entry.Log.Id)
            .Skip((page - 1) * AuditLogQuery.PageSize)
            .Take(AuditLogQuery.PageSize)
            .Select(entry => new AuditLogEntryDto(
                entry.Log.Id,
                entry.Log.TimestampUtc,
                entry.Log.UserId,
                entry.Email,
                entry.Log.Action,
                entry.Log.EntityType,
                entry.Log.EntityId,
                entry.Log.Details))
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditLogEntryDto>(items, page, AuditLogQuery.PageSize, totalCount);
    }

    public async Task<AuditLogFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken = default) =>
        new(
            await _context.AuditLogs.AsNoTracking().Select(log => log.Action).Distinct().OrderBy(action => action).ToListAsync(cancellationToken),
            await _context.AuditLogs.AsNoTracking().Select(log => log.EntityType).Distinct().OrderBy(type => type).ToListAsync(cancellationToken));
}
