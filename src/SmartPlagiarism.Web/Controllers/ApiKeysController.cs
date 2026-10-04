using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.Identity;
using SmartPlagiarism.Web.Models.Admin;

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
    public async Task<IActionResult> Index(int page = 1, CancellationToken cancellationToken = default) =>
        View(await _apiKeys.GetKeysAsync(page, cancellationToken));

    /// <summary>
    /// Shows the new key on the response itself rather than redirecting: the key
    /// exists only here, and a redirect would need it stored somewhere - TempData
    /// is a cookie - to show it on the next page.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ApiKeyFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            TempData["StatusMessage"] = ModelState.ErrorSummary();
            return RedirectToAction(nameof(Index));
        }

        var (result, key) = await _apiKeys.CreateAsync(model.Name, CurrentUserId, cancellationToken);

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
