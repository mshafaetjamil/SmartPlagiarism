using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using SmartPlagiarism.Web.ErrorHandling;
using SmartPlagiarism.Web.Models;

namespace SmartPlagiarism.Web.Controllers;

/// <summary>
/// The friendly error page. Nothing links here: the exception handler
/// re-executes /Error after an unhandled exception, and status code pages
/// re-execute /Error/{status} for an error response with no body.
///
/// Open to anyone and to every HTTP method, with no antiforgery check: a POST
/// that failed is re-executed as a POST, perhaps for a signed-out user, and its
/// error page must still render.
/// </summary>
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class ErrorController : Controller
{
    [Route("Error/{statusCode:int?}")]
    public IActionResult Index(int? statusCode)
    {
        // The middleware's own status beats the one in the URL, which only
        // matters when someone types /Error/... into the address bar.
        var status = HttpContext.Features.Get<IExceptionHandlerFeature>() is not null
            ? Response.StatusCode
            : HttpContext.Features.Get<IStatusCodeReExecuteFeature>()?.OriginalStatusCode
                ?? statusCode
                ?? StatusCodes.Status404NotFound;

        if (status is < 400 or > 599)
        {
            status = StatusCodes.Status404NotFound;
        }

        Response.StatusCode = status;

        return View("Error", ErrorViewModel.For(status, ErrorHandlingExtensions.TraceId(HttpContext)));
    }
}
