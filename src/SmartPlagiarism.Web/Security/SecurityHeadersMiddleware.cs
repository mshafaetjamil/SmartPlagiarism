namespace SmartPlagiarism.Web.Security;

/// <summary>
/// Response headers that have the browser refuse whole classes of attack, set
/// on every response.
///
/// The Content-Security-Policy is the strict one: scripts, styles, fonts and
/// requests from this site only, and no inline script or style anywhere - the
/// views keep their behaviour in wwwroot/js and their looks in wwwroot/css. So
/// even text that somehow got past Razor's HTML encoding could not run.
///
/// Set before the rest of the pipeline runs rather than as the response starts,
/// so the developer exception page - which clears the response and relies on
/// inline script - is left without them, while the error page this app renders,
/// which goes back through this middleware, gets them again.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; "
        + "script-src 'self'; "
        + "style-src 'self'; "

        // Bootstrap draws its checkboxes, selects and navbar icon with data: SVGs.
        + "img-src 'self' data:; "
        + "font-src 'self'; "
        + "connect-src 'self'; "
        + "object-src 'none'; "
        + "base-uri 'self'; "
        + "form-action 'self'; "
        + "frame-ancestors 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        headers.ContentSecurityPolicy = ContentSecurityPolicy;

        // Serve a file as the type it was declared, never as one guessed from its bytes.
        headers.XContentTypeOptions = "nosniff";

        // No framing, so no clickjacking; frame-ancestors says the same to newer browsers.
        headers.XFrameOptions = "DENY";

        // Submission ids in addresses stay on this site.
        headers["Referrer-Policy"] = "same-origin";

        // Nothing here needs the camera, microphone or location.
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";

        // A page opened from elsewhere gets no handle on this one's window.
        headers["Cross-Origin-Opener-Policy"] = "same-origin";

        return next(context);
    }
}

public static class SecurityHeadersExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.UseMiddleware<SecurityHeadersMiddleware>();
}
