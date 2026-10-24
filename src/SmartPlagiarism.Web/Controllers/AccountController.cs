using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.Auditing;
using SmartPlagiarism.Core.Identity;
using SmartPlagiarism.Web.Models.Account;
using SmartPlagiarism.Web.Security;

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
    /// <summary>
    /// Every failure reads the same - unknown account, wrong password, locked or
    /// deactivated - so the page cannot be used to find out which accounts exist.
    /// The audit log keeps the real reason.
    /// </summary>
    private const string InvalidCredentialsMessage =
        "Invalid email or password. Repeated failures lock an account for a few minutes.";

    private const string ThrottledMessage =
        "Too many failed sign-in attempts from this network. Try again later.";

    /// <summary>The hash checked against when there is no real one; made on first use.</summary>
    private static string? s_unusedPasswordHash;

    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditService _auditService;
    private readonly SignInThrottle _throttle;

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IAuditService auditService,
        SignInThrottle throttle)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _auditService = auditService;
        _throttle = throttle;
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

        var address = HttpContext.Connection.RemoteIpAddress;

        // Refused before any account is looked at. Not audited: an address that
        // got here is already trying passwords faster than any person types them,
        // and a row per attempt would only fill the audit log for it.
        if (_throttle.IsBlocked(address))
        {
            ModelState.AddModelError(string.Empty, ThrottledMessage);
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
            // An unknown, locked or deactivated account is refused without its
            // password being checked, which takes measurable time; checking one
            // anyway keeps the response time from telling those apart.
            if (user is null || result.IsLockedOut || result.IsNotAllowed)
            {
                CheckUnusedPassword(model.Password);
            }

            _throttle.RecordFailure(address);

            await _auditService.RecordAsync(
                new AuditEntry(
                    AuditActions.LoginFailed,
                    AuditEntityTypes.Account,
                    user?.Id,
                    user?.Id,
                    $"Attempted email: {model.Email}. Reason: {DescribeFailure(result)}. IP: {ClientIpAddress()}"),
                cancellationToken);

            ModelState.AddModelError(string.Empty, InvalidCredentialsMessage);

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

    /// <summary>Does the work of checking a password, against a hash no account has.</summary>
    private void CheckUnusedPassword(string password)
    {
        var hasher = _userManager.PasswordHasher;
        var unused = new ApplicationUser();

        s_unusedPasswordHash ??= hasher.HashPassword(unused, Guid.NewGuid().ToString("N"));
        hasher.VerifyHashedPassword(unused, s_unusedPasswordHash, password);
    }

    /// <summary>
    /// Only ever redirects inside this application - an attacker-supplied
    /// returnUrl pointing at another host is discarded.
    /// </summary>
    private IActionResult RedirectToLocal(string? returnUrl) =>
        Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl)
            : RedirectToAction(nameof(DashboardController.Index), "Dashboard");
}
