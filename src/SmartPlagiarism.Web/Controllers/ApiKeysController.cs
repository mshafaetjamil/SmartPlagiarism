using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.Identity;

namespace SmartPlagiarism.Web.Controllers;

/// <summary>Admin: keys for systems integrating over the REST API.</summary>
[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
public class ApiKeysController : Controller
{
    private readonly IApiKeyService _apiKeys;

    public ApiKeysController(IApiKeyService apiKeys)
    {
        _apiKeys = apiKeys;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await _apiKeys.GetKeysAsync(cancellationToken));

    /// <summary>
    /// Shows the new key on the response itself rather than redirecting: the key
    /// exists only here, and a redirect would need it stored somewhere - TempData
    /// is a cookie - to show it on the next page.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, CancellationToken cancellationToken)
    {
        var (result, key) = await _apiKeys.CreateAsync(name, CurrentUserId, cancellationToken);

        if (!result.Succeeded)
        {
            TempData["StatusMessage"] = string.Join(" ", result.Errors);
            return RedirectToAction(nameof(Index));
        }

        return View("Created", key);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(int id, CancellationToken cancellationToken)
    {
        var result = await _apiKeys.RevokeAsync(id, CurrentUserId, cancellationToken);

        TempData["StatusMessage"] = result.Succeeded
            ? "Key revoked. Requests using it are refused from now on."
            : string.Join(" ", result.Errors);

        return RedirectToAction(nameof(Index));
    }
}
