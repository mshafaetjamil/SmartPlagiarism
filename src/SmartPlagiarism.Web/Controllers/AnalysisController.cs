using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.DTOs.Analysis;
using SmartPlagiarism.Core.Identity;
using SmartPlagiarism.Web.Models.Analysis;

namespace SmartPlagiarism.Web.Controllers;

/// <summary>
/// Teacher controls over the background analysis: re-running it, and the status
/// endpoint the teacher dashboard polls.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.TeacherOnly)]
public class AnalysisController : Controller
{
    private readonly IAnalysisService _analysisService;

    public AnalysisController(IAnalysisService analysisService)
    {
        _analysisService = analysisService;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rerun(int id, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var outcome = await _analysisService.RequeueAsync(id, userId, cancellationToken);

        TempData["StatusMessage"] = outcome switch
        {
            RequeueOutcome.Requeued => "Analysis has been queued to run again.",
            RequeueOutcome.AlreadyPending => "That submission is already waiting for or undergoing analysis.",
            _ => "That submission no longer exists.",
        };

        return RedirectToAction(nameof(DashboardController.Teacher), "Dashboard");
    }

    /// <inheritdoc cref="SubmissionsController.Status"/>
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Status([FromQuery] int[] ids, CancellationToken cancellationToken)
    {
        var statuses = await _analysisService.GetStatusesAsync(ids, cancellationToken);

        return Json(AnalysisStatusResponse.From(statuses));
    }
}
