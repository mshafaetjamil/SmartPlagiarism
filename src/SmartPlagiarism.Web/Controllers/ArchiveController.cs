using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.DTOs.Archive;
using SmartPlagiarism.Core.Identity;
using SmartPlagiarism.Web.Models.Admin;

namespace SmartPlagiarism.Web.Controllers;

/// <summary>Admin: the archive of past students' work that submissions are matched against.</summary>
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public class ArchiveController : Controller
{
    /// <summary>The package plus room for the rest of the form.</summary>
    private const long MaxImportRequestBytes = ArchiveLimits.MaxPackageBytes + (1024 * 1024);

    private readonly IArchiveService _archive;
    private readonly IAcademicStructureService _academicStructure;

    public ArchiveController(IArchiveService archive, IAcademicStructureService academicStructure)
    {
        _archive = archive;
        _academicStructure = academicStructure;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] ArchiveFilter filter, CancellationToken cancellationToken)
    {
        var overview = await _archive.GetOverviewAsync(cancellationToken);
        var documents = await _archive.GetDocumentsAsync(filter.ToQuery(), cancellationToken);
        var departments = await _academicStructure.GetDepartmentsAsync(cancellationToken);

        return View(new ArchiveViewModel(filter, overview, documents, departments));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxImportRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxImportRequestBytes)]
    public async Task<IActionResult> Import(ArchiveImportForm form, CancellationToken cancellationToken)
    {
        // The year and department are checked by the service, against the
        // archive's range and the departments that exist.
        if (!ModelState.IsValid || form.Package is null)
        {
            TempData["StatusMessage"] = ModelState.ErrorSummary();
            return RedirectToAction(nameof(Index));
        }

        await using var content = form.Package.OpenReadStream();
        var package = new ArchivePackage(form.Package.FileName, form.Package.Length, content, form.DefaultYear, form.DefaultDepartmentId);
        var result = await _archive.QueueImportAsync(package, CurrentUserId, cancellationToken);

        TempData["StatusMessage"] = result.Succeeded
            ? $"{form.Package.FileName} is being imported. Progress is shown below; you can leave this page."
            : string.Join(" ", result.Errors);

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, string? returnUrl, CancellationToken cancellationToken)
    {
        var result = await _archive.DeleteDocumentAsync(id, CurrentUserId, cancellationToken);

        TempData["StatusMessage"] = result.Succeeded
            ? "Document removed from the archive."
            : string.Join(" ", result.Errors);

        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction(nameof(Index));
    }
}
