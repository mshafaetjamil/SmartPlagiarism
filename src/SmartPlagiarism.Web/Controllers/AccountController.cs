using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.Auditing;
using SmartPlagiarism.Core.Identity;
using SmartPlagiarism.Web.Models.Account;

// Microsoft.AspNetCore.Mvc also defines a SignInResult, and Controller pulls it
// into scope; this is the Identity one.
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace SmartPlagiarism.Web.Controllers;

/// <summary>
/// Sign-in and sign-out. There is deliberately no Register action: accounts are
/// created by an administrator (Phase 8), never by public signup.
/// </summary>
[Authorize]
public class AccountController : Controller
{
    private const string InvalidCredentialsMessage = "Invalid email or password.";

    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditService _auditService;

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IAuditService auditService)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _auditService = auditService;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction(nameof(DashboardController.Index), "Dashboard");
        }

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userManager.FindByEmailAsync(model.Email);

        // Looking the user up first, then signing in by entity, keeps this working
        // even if usernames stop matching emails later.
        var result = user is null
            ? SignInResult.Failed
            : await _signInManager.PasswordSignInAsync(user, model.Password, model.RememberMe, lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            await _auditService.RecordAsync(
                new AuditEntry(
                    AuditActions.LoginFailed,
                    AuditEntityTypes.Account,
                    user?.Id,
                    user?.Id,
                    $"Attempted email: {model.Email}. Reason: {DescribeFailure(result)}. IP: {ClientIpAddress()}"),
                cancellationToken);

            // The same message whether the account exists or not, so this page
            // cannot be used to enumerate valid accounts.
            ModelState.AddModelError(
                string.Empty,
                result.IsLockedOut
                    ? "This account is locked out. Try again later or contact an administrator."
                    : InvalidCredentialsMessage);

            return View(model);
        }

        await _auditService.RecordAsync(
            new AuditEntry(AuditActions.Login, AuditEntityTypes.Account, user!.Id, user.Id, $"IP: {ClientIpAddress()}"),
            cancellationToken);

        return RedirectToLocal(model.ReturnUrl);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var userId = _userManager.GetUserId(User);

        await _signInManager.SignOutAsync();

        await _auditService.RecordAsync(
            new AuditEntry(AuditActions.Logout, AuditEntityTypes.Account, userId, userId, $"IP: {ClientIpAddress()}"),
            cancellationToken);

        return RedirectToAction(nameof(HomeController.Index), "Home");
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied()
    {
        return View();
    }

    private static string DescribeFailure(SignInResult result) => result switch
    {
        { IsLockedOut: true } => "locked out",
        { IsNotAllowed: true } => "not allowed to sign in",
        { RequiresTwoFactor: true } => "two-factor required",
        _ => "invalid credentials",
    };

    private string ClientIpAddress() =>
        HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary>
    /// Only ever redirects inside this application - an attacker-supplied
    /// returnUrl pointing at another host is discarded.
    /// </summary>
    private IActionResult RedirectToLocal(string? returnUrl) =>
        Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl)
            : RedirectToAction(nameof(DashboardController.Index), "Dashboard");
}
