using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.DTOs.Submissions;
using SmartPlagiarism.Core.Files;
using SmartPlagiarism.Web.Models;
using SmartPlagiarism.Core.Identity;
using SmartPlagiarism.Web.Authorization;
using SmartPlagiarism.Web.Models.Analysis;
using SmartPlagiarism.Web.Models.Submissions;

namespace SmartPlagiarism.Web.Controllers;

/// <summary>
/// The student module: create submissions, see their history, open one.
/// Every action is scoped to the signed-in student by the service layer.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.StudentOnly)]
public class SubmissionsController : Controller
{
    private readonly ISubmissionService _submissionService;
    private readonly IReportService _reportService;
    private readonly IAuthorizationService _authorization;

    public SubmissionsController(
        ISubmissionService submissionService,
        IReportService reportService,
        IAuthorizationService authorization)
    {
        _submissionService = submissionService;
        _reportService = reportService;
        _authorization = authorization;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        return View(await _submissionService.GetForStudentAsync(CurrentUserId, cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        return View(await BuildFormAsync(new CreateSubmissionViewModel(), cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(UploadConstraints.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadConstraints.MaxRequestBytes)]
    public async Task<IActionResult> Create(CreateSubmissionViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(await BuildFormAsync(model, cancellationToken));
        }

        // The streams stay open only for the duration of the call, and this
        // controller does no file IO itself.
        await using var files = FormFileUploads.Open(model.Files);

        var result = await _submissionService.CreateAsync(
            new CreateSubmissionRequest(
                CurrentUserId,
                model.Title,
                model.Type,
                model.CourseId,
                model.SemesterId,
                files.Uploads),
            cancellationToken);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            return View(await BuildFormAsync(model, cancellationToken));
        }

        TempData["StatusMessage"] = "Submission received. Analysis has been queued.";
        return RedirectToAction(nameof(Details), new { id = result.SubmissionId });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        if (!await CanViewAsync(id))
        {
            return NotFound();
        }

        var submission = await _submissionService.GetDetailForStudentAsync(id, CurrentUserId, cancellationToken);

        // Null covers both "no such submission" and "not yours" - the student cannot
        // tell the difference, so this page leaks nothing about others' work.
        if (submission is null)
        {
            return NotFound();
        }

        return View(submission);
    }

    /// <summary>The student's copy of the originality report, as a PDF download.</summary>
    [HttpGet]
    public async Task<IActionResult> Report(int id, CancellationToken cancellationToken)
    {
        if (!await CanViewAsync(id))
        {
            return NotFound();
        }

        // The service checks access again itself; a student's copy never names
        // or quotes the other students matched.
        var report = await _reportService.GetSubmissionReportAsync(
            id,
            new SubmissionViewer(CurrentUserId, IsStaff: false),
            cancellationToken);

        return report is null ? NotFound() : File(report.Content, report.ContentType, report.FileName);
    }

    /// <summary>
    /// Analysis status of the listed submissions, for the page's poller. Only the
    /// student's own submissions are returned; never cached, since polling a
    /// stale answer would stall the page.
    /// </summary>
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Status([FromQuery] int[] ids, CancellationToken cancellationToken)
    {
        var statuses = await _submissionService.GetAnalysisStatusesForStudentAsync(CurrentUserId, ids, cancellationToken);

        return Json(AnalysisStatusResponse.From(statuses));
    }

    /// <summary>
    /// The resource check for "your own submissions only". Not found rather than
    /// forbidden, so a student cannot tell another student's id from a missing one.
    /// </summary>
    private async Task<bool> CanViewAsync(int submissionId) =>
        (await _authorization.AuthorizeAsync(
            User,
            new SubmissionResource(submissionId),
            AuthorizationPolicies.CanViewSubmission)).Succeeded;

    private async Task<CreateSubmissionViewModel> BuildFormAsync(
        CreateSubmissionViewModel model,
        CancellationToken cancellationToken)
    {
        var options = await _submissionService.GetFormOptionsAsync(cancellationToken);

        model.Courses = options.Courses;
        model.Semesters = options.Semesters;

        return model;
    }
}
