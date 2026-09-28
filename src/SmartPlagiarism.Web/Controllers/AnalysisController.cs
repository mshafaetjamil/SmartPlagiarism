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

    /// <param name="returnUrl">
    /// Where to go back to - the submission page or the dashboard. Only local
    /// URLs are followed, so the parameter cannot be used as an open redirect.
    /// </param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rerun(int id, string? returnUrl, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var outcome = await _analysisService.RequeueAsync(id, SubmissionViewer.ForTeacher(userId), cancellationToken);

        TempData["StatusMessage"] = outcome switch
        {
            RequeueOutcome.Requeued => "Analysis has been queued to run again.",
            RequeueOutcome.AlreadyPending => "That submission is already waiting for or undergoing analysis.",
            _ => "That submission no longer exists.",
        };

        return Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction(nameof(DashboardController.Teacher), "Dashboard");
    }

    /// <inheritdoc cref="SubmissionsController.Status"/>
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Status([FromQuery] int[] ids, CancellationToken cancellationToken)
    {
        var statuses = await _analysisService.GetStatusesAsync(
            ids,
            SubmissionViewer.ForTeacher(User.FindFirstValue(ClaimTypes.NameIdentifier)!),
            cancellationToken);

        return Json(AnalysisStatusResponse.From(statuses));
    }
}
