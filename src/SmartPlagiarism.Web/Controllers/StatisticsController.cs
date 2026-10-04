using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.DTOs.Analysis;
using SmartPlagiarism.Core.Identity;
using SmartPlagiarism.Web.Models.Admin;

namespace SmartPlagiarism.Web.Controllers;

/// <summary>Admin: system statistics, and retrying failed analyses.</summary>
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public class StatisticsController : Controller
{
    private readonly IStatisticsService _statistics;
    private readonly IAnalysisService _analysis;

    public StatisticsController(IStatisticsService statistics, IAnalysisService analysis)
    {
        _statistics = statistics;
        _analysis = analysis;
    }

    /// <param name="page">The page of the failed-analyses list.</param>
    [HttpGet]
    public async Task<IActionResult> Index(int page = 1, CancellationToken cancellationToken = default) =>
        View(await _statistics.GetStatisticsAsync(failedPage: page, cancellationToken: cancellationToken));

    /// <summary>Queues a failed analysis to run again - after OCR is installed, say.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Retry(int id, CancellationToken cancellationToken)
    {
        var outcome = await _analysis.RequeueAsync(id, SubmissionViewer.ForAdmin(User.FindFirstValue(ClaimTypes.NameIdentifier)!), cancellationToken);

        TempData["StatusMessage"] = outcome switch
        {
            RequeueOutcome.Requeued => $"Submission #{id} has been queued for analysis again.",
            RequeueOutcome.AlreadyPending => $"Submission #{id} is already waiting for analysis.",
            _ => $"Submission #{id} no longer exists.",
        };

        return RedirectToAction(nameof(Index));
    }
}

/// <summary>Admin: the audit trail, filtered and paged.</summary>
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public class AuditLogController : Controller
{
    private readonly IAuditLogReader _auditLog;

    public AuditLogController(IAuditLogReader auditLog)
    {
        _auditLog = auditLog;
    }

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] AuditLogFilter filter, CancellationToken cancellationToken) =>
        View(new AuditLogViewModel(
            filter,
            await _auditLog.GetEntriesAsync(filter.ToQuery(), cancellationToken),
            await _auditLog.GetFilterOptionsAsync(cancellationToken)));
}
