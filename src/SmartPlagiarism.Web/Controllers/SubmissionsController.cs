using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.DTOs.Submissions;
using SmartPlagiarism.Core.Files;
using SmartPlagiarism.Core.Identity;
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

    public SubmissionsController(ISubmissionService submissionService)
    {
        _submissionService = submissionService;
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

        // Mapping IFormFile to Core's transport type. The streams stay open only for
        // the duration of the call, and this controller does no file IO itself.
        var streams = new List<Stream>(model.Files.Count);

        try
        {
            var uploads = new List<FileUpload>(model.Files.Count);

            foreach (var formFile in model.Files)
            {
                var stream = formFile.OpenReadStream();
                streams.Add(stream);
                uploads.Add(new FileUpload(formFile.FileName, formFile.Length, stream));
            }

            var result = await _submissionService.CreateAsync(
                new CreateSubmissionRequest(
                    CurrentUserId,
                    model.Title,
                    model.Type,
                    model.CourseId,
                    model.SemesterId,
                    uploads),
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
        finally
        {
            foreach (var stream in streams)
            {
                await stream.DisposeAsync();
            }
        }
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var submission = await _submissionService.GetDetailForStudentAsync(id, CurrentUserId, cancellationToken);

        // Null covers both "no such submission" and "not yours" - the student cannot
        // tell the difference, so this page leaks nothing about others' work.
        if (submission is null)
        {
            return NotFound();
        }

        return View(submission);
    }

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
