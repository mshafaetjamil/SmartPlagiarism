using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.DTOs.Admin;
using SmartPlagiarism.Core.Identity;
using SmartPlagiarism.Web.Models.Admin;

namespace SmartPlagiarism.Web.Controllers;

/// <summary>
/// What the three academic-structure controllers share: who is acting, and
/// turning a service result into a redirect or a redisplayed form.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public abstract class AcademicControllerBase : Controller
{
    protected string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    /// <summary>After a create or edit: back to the list, or the form again with the reasons.</summary>
    protected async Task<IActionResult> AfterSaveAsync(AdminResult result, string done, Func<Task<IActionResult>> redisplay)
    {
        if (result.Succeeded)
        {
            TempData["StatusMessage"] = done;
            return RedirectToAction("Index");
        }

        ModelState.AddErrors(result);
        return await redisplay();
    }

    /// <summary>After a delete: back to the list, saying what happened.</summary>
    protected IActionResult AfterDelete(AdminResult result, string done)
    {
        TempData["StatusMessage"] = result.Succeeded ? done : string.Join(" ", result.Errors);
        return RedirectToAction("Index");
    }
}

/// <summary>Admin: departments.</summary>
public class DepartmentsController(IAcademicStructureService academic) : AcademicControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await academic.GetDepartmentsAsync(cancellationToken));

    [HttpGet]
    public IActionResult Create() => View("Form", new DepartmentFormViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DepartmentFormViewModel model, CancellationToken cancellationToken) =>
        !ModelState.IsValid
            ? View("Form", model)
            : await AfterSaveAsync(
                await academic.CreateDepartmentAsync(model.ToInput(), CurrentUserId, cancellationToken),
                $"Department {model.Code.Trim().ToUpperInvariant()} created.",
                () => Task.FromResult<IActionResult>(View("Form", model)));

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var department = await academic.GetDepartmentAsync(id, cancellationToken);

        return department is null
            ? NotFound()
            : View("Form", new DepartmentFormViewModel { Id = department.Id, Code = department.Code, Name = department.Name });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, DepartmentFormViewModel model, CancellationToken cancellationToken)
    {
        model.Id = id;

        return !ModelState.IsValid
            ? View("Form", model)
            : await AfterSaveAsync(
                await academic.UpdateDepartmentAsync(id, model.ToInput(), CurrentUserId, cancellationToken),
                $"Department {model.Code.Trim().ToUpperInvariant()} saved.",
                () => Task.FromResult<IActionResult>(View("Form", model)));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken) =>
        AfterDelete(await academic.DeleteDepartmentAsync(id, CurrentUserId, cancellationToken), "Department deleted.");
}

/// <summary>Admin: courses.</summary>
public class CoursesController(IAcademicStructureService academic) : AcademicControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await academic.GetCoursesAsync(cancellationToken));

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken) =>
        await FormAsync(new CourseFormViewModel(), cancellationToken);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CourseFormViewModel model, CancellationToken cancellationToken) =>
        !ModelState.IsValid
            ? await FormAsync(model, cancellationToken)
            : await AfterSaveAsync(
                await academic.CreateCourseAsync(model.ToInput(), CurrentUserId, cancellationToken),
                $"Course {model.Code.Trim().ToUpperInvariant()} created.",
                () => FormAsync(model, cancellationToken));

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var course = await academic.GetCourseAsync(id, cancellationToken);

        return course is null
            ? NotFound()
            : await FormAsync(
                new CourseFormViewModel { Id = course.Id, DepartmentId = course.DepartmentId, Code = course.Code, Title = course.Title },
                cancellationToken);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, CourseFormViewModel model, CancellationToken cancellationToken)
    {
        model.Id = id;

        return !ModelState.IsValid
            ? await FormAsync(model, cancellationToken)
            : await AfterSaveAsync(
                await academic.UpdateCourseAsync(id, model.ToInput(), CurrentUserId, cancellationToken),
                $"Course {model.Code.Trim().ToUpperInvariant()} saved.",
                () => FormAsync(model, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken) =>
        AfterDelete(await academic.DeleteCourseAsync(id, CurrentUserId, cancellationToken), "Course deleted.");

    private async Task<IActionResult> FormAsync(CourseFormViewModel model, CancellationToken cancellationToken)
    {
        model.Departments = await academic.GetDepartmentsAsync(cancellationToken);
        return View("Form", model);
    }
}

/// <summary>Admin: semesters.</summary>
public class SemestersController(IAcademicStructureService academic) : AcademicControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await academic.GetSemestersAsync(cancellationToken));

    [HttpGet]
    public IActionResult Create() => View("Form", new SemesterFormViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SemesterFormViewModel model, CancellationToken cancellationToken) =>
        !ModelState.IsValid
            ? View("Form", model)
            : await AfterSaveAsync(
                await academic.CreateSemesterAsync(model.ToInput(), CurrentUserId, cancellationToken),
                $"Semester {model.Name} created.",
                () => Task.FromResult<IActionResult>(View("Form", model)));

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var semester = await academic.GetSemesterAsync(id, cancellationToken);

        return semester is null
            ? NotFound()
            : View("Form", new SemesterFormViewModel
            {
                Id = semester.Id,
                Name = semester.Name,
                StartDate = DateOnly.FromDateTime(semester.StartDateUtc),
                EndDate = DateOnly.FromDateTime(semester.EndDateUtc),
            });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, SemesterFormViewModel model, CancellationToken cancellationToken)
    {
        model.Id = id;

        return !ModelState.IsValid
            ? View("Form", model)
            : await AfterSaveAsync(
                await academic.UpdateSemesterAsync(id, model.ToInput(), CurrentUserId, cancellationToken),
                $"Semester {model.Name} saved.",
                () => Task.FromResult<IActionResult>(View("Form", model)));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken) =>
        AfterDelete(await academic.DeleteSemesterAsync(id, CurrentUserId, cancellationToken), "Semester deleted.");
}
