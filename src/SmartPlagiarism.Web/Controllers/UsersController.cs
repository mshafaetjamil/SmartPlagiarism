using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.Identity;
using SmartPlagiarism.Web.Models.Admin;

namespace SmartPlagiarism.Web.Controllers;

/// <summary>Admin: accounts - list, create with a role, deactivate and reactivate.</summary>
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public class UsersController : Controller
{
    private readonly IUserAdministrationService _users;
    private readonly IAcademicStructureService _academic;

    public UsersController(IUserAdministrationService users, IAcademicStructureService academic)
    {
        _users = users;
        _academic = academic;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] UserListFilter filter, CancellationToken cancellationToken)
    {
        var results = await _users.GetUsersAsync(filter.ToQuery(), cancellationToken);

        return View(new UserListViewModel(filter, results, CurrentUserId));
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken) =>
        View(new CreateUserViewModel { Departments = await _academic.GetDepartmentsAsync(cancellationToken) });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateUserViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await _users.CreateAsync(model.ToRequest(), CurrentUserId, cancellationToken);

            if (result.Succeeded)
            {
                TempData["StatusMessage"] = $"{model.Role} account created for {model.Email}.";
                return RedirectToAction(nameof(Index));
            }

            ModelState.AddErrors(result);
        }

        model.Departments = await _academic.GetDepartmentsAsync(cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Deactivate(string id, string? returnUrl, CancellationToken cancellationToken) =>
        SetActiveAsync(id, active: false, returnUrl, cancellationToken);

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> Reactivate(string id, string? returnUrl, CancellationToken cancellationToken) =>
        SetActiveAsync(id, active: true, returnUrl, cancellationToken);

    private async Task<IActionResult> SetActiveAsync(string id, bool active, string? returnUrl, CancellationToken cancellationToken)
    {
        var result = await _users.SetActiveAsync(id, active, CurrentUserId, cancellationToken);

        TempData["StatusMessage"] = result.Succeeded
            ? active ? "Account reactivated." : "Account deactivated; the user is signed out within a minute."
            : string.Join(" ", result.Errors);

        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction(nameof(Index));
    }
}
