using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.DTOs.Submissions;
using SmartPlagiarism.Core.Enums;
using SmartPlagiarism.Core.Files;
using SmartPlagiarism.Core.Identity;
using SmartPlagiarism.Web.Authorization;
using SmartPlagiarism.Web.Models;
using SmartPlagiarism.Web.Models.Api.V1;

namespace SmartPlagiarism.Web.Controllers.Api.V1;

/// <summary>
/// Submissions for the university portal: upload, status, report.
///
/// Everything goes through the same Core services as the web UI - the same
/// validation, storage, analysis queue, access rule and report - so the API
/// can never behave differently from the site. A key sees only the submissions
/// it created.
/// </summary>
[Route("api/v1/submissions")]
[Authorize(Policy = AuthorizationPolicies.ApiClient)]
public class SubmissionsApiController : ApiControllerBase
{
    private readonly ISubmissionService _submissions;
    private readonly IReportService _reports;

    public SubmissionsApiController(ISubmissionService submissions, IReportService reports)
    {
        _submissions = submissions;
        _reports = reports;
    }

    private SubmissionViewer Viewer => SubmissionViewer.ForApiClient(ApiKeyAuthenticationHandler.ClientOf(User));

    /// <summary>
    /// Creates a submission for a student and queues its analysis. Returns 202:
    /// the analysis runs in the background, so poll the Location URL for its result.
    /// </summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [EnableRateLimiting(ApiRateLimits.Uploads)]
    [RequestSizeLimit(UploadConstraints.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadConstraints.MaxRequestBytes)]
    [ProducesResponseType<SubmissionApiResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Create([FromForm] CreateSubmissionApiRequest request, CancellationToken cancellationToken)
    {
        var studentUserId = await _submissions.FindStudentUserIdAsync(request.StudentNumber, cancellationToken);

        if (studentUserId is null)
        {
            ModelState.AddModelError(nameof(request.StudentNumber), "No student has that student number.");
            return ValidationProblem(ModelState);
        }

        await using var files = FormFileUploads.Open(request.Files);

        var result = await _submissions.CreateAsync(
            new CreateSubmissionRequest(
                studentUserId,
                request.Title,
                request.Type,
                request.CourseId,
                request.SemesterId,
                files.Uploads,
                Viewer.ApiKeyId),
            cancellationToken);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError("submission", error);
            }

            return ValidationProblem(ModelState);
        }

        var created = await _submissions.GetDetailAsync(result.SubmissionId, Viewer, cancellationToken);

        return AcceptedAtAction(nameof(Get), new { id = result.SubmissionId }, Response(created!));
    }

    /// <summary>The submission's analysis status and, once analysed, its similarity result.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<SubmissionApiResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        var detail = await _submissions.GetDetailAsync(id, Viewer, cancellationToken);

        return detail is null ? NotFound() : Ok(Response(detail));
    }

    /// <summary>
    /// The originality report as a PDF - the student's copy, which never names or
    /// quotes other students. 409 until the analysis has finished.
    /// </summary>
    [HttpGet("{id:int}/report")]
    [Produces("application/pdf", "application/problem+json")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK, "application/pdf")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetReport(int id, CancellationToken cancellationToken)
    {
        var detail = await _submissions.GetDetailAsync(id, Viewer, cancellationToken);

        if (detail is null)
        {
            return NotFound();
        }

        if (detail.AnalysisStatus is AnalysisStatus.Queued or AnalysisStatus.Processing)
        {
            return Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "The analysis has not finished yet.",
                detail: $"Submission {id} is {detail.AnalysisStatus}. Poll GET /api/v1/submissions/{id} until it is Completed or Failed.");
        }

        var report = await _reports.GetSubmissionReportAsync(id, Viewer, cancellationToken);

        return report is null ? NotFound() : File(report.Content, report.ContentType, report.FileName);
    }

    private new SubmissionApiResponse Response(SubmissionDetailDto detail) => SubmissionApiResponse.From(
        detail,
        new SubmissionApiLinks(
            Url.Action(nameof(Get), new { id = detail.Id })!,
            Url.Action(nameof(GetReport), new { id = detail.Id })!));
}
