using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.Identity;

namespace SmartPlagiarism.Web.Controllers;

/// <summary>
/// The post-login landing point. <see cref="Index"/> forwards to whichever
/// dashboard the signed-in user's role entitles them to.
/// </summary>
[Authorize]
public class DashboardController : Controller
{
    private readonly IDashboardService _dashboardService;

    public DashboardController(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    [HttpGet]
    public IActionResult Index()
    {
        // Most privileged role wins, so an account holding several roles lands on
        // the broadest dashboard rather than the narrowest.
        if (User.IsInRole(ApplicationRoles.Admin))
        {
            return RedirectToAction(nameof(Admin));
        }

        if (User.IsInRole(ApplicationRoles.Teacher))
        {
            return RedirectToAction(nameof(Teacher));
        }

        if (User.IsInRole(ApplicationRoles.Student))
        {
            return RedirectToAction(nameof(Student));
        }

        // Authenticated but holding none of the three roles.
        return RedirectToAction(nameof(AccountController.AccessDenied), "Account");
    }

    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.StudentOnly)]
    public async Task<IActionResult> Student(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        return View(await _dashboardService.GetStudentDashboardAsync(userId, cancellationToken));
    }

    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.TeacherOnly)]
    public async Task<IActionResult> Teacher(CancellationToken cancellationToken)
    {
        return View(await _dashboardService.GetTeacherDashboardAsync(cancellationToken));
    }

    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.AdminOnly)]
    public async Task<IActionResult> Admin(CancellationToken cancellationToken)
    {
        return View(await _dashboardService.GetAdminDashboardAsync(cancellationToken));
    }
}
