using Microsoft.EntityFrameworkCore;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.Auditing;
using SmartPlagiarism.Core.DTOs.Analysis;
using SmartPlagiarism.Core.Enums;
using SmartPlagiarism.Infrastructure.Data;
using SmartPlagiarism.Infrastructure.Services;

namespace SmartPlagiarism.Infrastructure.Analysis;

/// <inheritdoc cref="IAnalysisService"/>
public class AnalysisService : IAnalysisService
{
    private readonly AppDbContext _context;
    private readonly IAnalysisQueue _queue;
    private readonly IAuditService _audit;

    public AnalysisService(AppDbContext context, IAnalysisQueue queue, IAuditService audit)
    {
        _context = context;
        _queue = queue;
        _audit = audit;
    }

    public async Task<RequeueOutcome> RequeueAsync(
        int submissionId,
        SubmissionViewer requestedBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestedBy);

        var submission = await _context.Submissions
            .VisibleToStaff(_context, requestedBy)
            .FirstOrDefaultAsync(candidate => candidate.Id == submissionId, cancellationToken);

        if (submission is null)
        {
            return RequeueOutcome.NotFound;
        }

        if (submission.AnalysisStatus is AnalysisStatus.Queued or AnalysisStatus.Processing)
        {
            return RequeueOutcome.AlreadyPending;
        }

        submission.RequeueForAnalysis();
        await _context.SaveChangesAsync(cancellationToken);

        // Saved before it is queued, so the worker never reads a stale status.
        await _queue.EnqueueAsync(submissionId, cancellationToken);

        await _audit.RecordAsync(
            new AuditEntry(
                AuditActions.AnalysisRequeued,
                AuditEntityTypes.Submission,
                submissionId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                requestedBy.UserId),
            cancellationToken);

        return RequeueOutcome.Requeued;
    }

    public async Task<IReadOnlyList<AnalysisStatusDto>> GetStatusesAsync(
        IReadOnlyCollection<int> submissionIds,
        SubmissionViewer viewer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(viewer);

        var ids = submissionIds.Distinct().Take(AnalysisStatusQuery.MaxSubmissionIds).ToList();

        if (ids.Count == 0)
        {
            return [];
        }

        return await _context.Submissions
            .AsNoTracking()
            .VisibleToStaff(_context, viewer)
            .Where(submission => ids.Contains(submission.Id))
            .Select(submission => new AnalysisStatusDto(submission.Id, submission.AnalysisStatus))
            .ToListAsync(cancellationToken);
    }
}
