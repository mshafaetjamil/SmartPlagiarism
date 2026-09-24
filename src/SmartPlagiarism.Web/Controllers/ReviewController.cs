using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.DTOs.Review;
using SmartPlagiarism.Core.Identity;
using SmartPlagiarism.Web.Models.Review;

namespace SmartPlagiarism.Web.Controllers;

/// <summary>
/// The teacher module: every submission, its analysis, the side-by-side match
/// viewer, and the review verdict.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.TeacherOnly)]
public class ReviewController : Controller
{
    private readonly IReviewService _reviewService;
    private readonly ISubmissionService _submissionService;
    private readonly IReportService _reportService;

    public ReviewController(
        IReviewService reviewService,
        ISubmissionService submissionService,
        IReportService reportService)
    {
        _reviewService = reviewService;
        _submissionService = submissionService;
        _reportService = reportService;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] ReviewListFilter filter, CancellationToken cancellationToken)
    {
        // An out-of-range similarity is reported by the view's validation messages
        // and clamped by the service, so the list still shows something sensible.
        var results = await _reviewService.GetSubmissionsAsync(filter.ToQuery(), cancellationToken);
        var options = await _submissionService.GetFormOptionsAsync(cancellationToken);

        return View(new ReviewListViewModel(filter, results, options.Courses, options.Semesters));
    }

    [HttpGet]
    public async Task<IActionResult> Submission(int id, CancellationToken cancellationToken)
    {
        var submission = await _reviewService.GetSubmissionAsync(id, cancellationToken);

        return submission is null ? NotFound() : View(submission);
    }

    [HttpGet]
    public async Task<IActionResult> Compare(int id, CancellationToken cancellationToken)
    {
        var comparison = await _reviewService.GetComparisonAsync(id, cancellationToken);

        return comparison is null ? NotFound() : View(comparison);
    }

    /// <summary>The staff copy of the originality report, naming and quoting every match.</summary>
    [HttpGet]
    public async Task<IActionResult> Report(int id, CancellationToken cancellationToken)
    {
        var report = await _reportService.GetSubmissionReportAsync(
            id,
            new SubmissionViewer(CurrentUserId, IsStaff: true),
            cancellationToken);

        return report is null ? NotFound() : File(report.Content, report.ContentType, report.FileName);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, ReviewDecisionForm form, CancellationToken cancellationToken)
    {
        var outcome = await _reviewService.ApproveAsync(id, form.Comment, CurrentUserId, cancellationToken);

        return AfterDecision(id, outcome, "Submission approved.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, ReviewDecisionForm form, CancellationToken cancellationToken)
    {
        var outcome = await _reviewService.RejectAsync(id, form.Comment, CurrentUserId, cancellationToken);

        return AfterDecision(id, outcome, "Submission rejected.");
    }

    private IActionResult AfterDecision(int id, ReviewDecisionOutcome outcome, string recorded)
    {
        if (outcome == ReviewDecisionOutcome.NotFound)
        {
            return NotFound();
        }

        TempData["StatusMessage"] = outcome switch
        {
            ReviewDecisionOutcome.Recorded => recorded,
            ReviewDecisionOutcome.AlreadyReviewed => "This submission has already been reviewed.",
            ReviewDecisionOutcome.CommentRequired => "A rejection needs a comment explaining why.",
            _ => $"The comment is too long; keep it under {Core.Entities.Submission.MaxTeacherCommentLength} characters.",
        };

        return RedirectToAction(nameof(Submission), new { id });
    }
}
