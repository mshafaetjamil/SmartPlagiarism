namespace SmartPlagiarism.Web.Models;

/// <summary>
/// What the error page says for a status code: plain words and a way forward,
/// never the exception itself.
/// </summary>
/// <param name="RequestId">The trace id the log entry for this request carries.</param>
public sealed record ErrorViewModel(int StatusCode, string Title, string Message, string? RequestId)
{
    /// <summary>
    /// Only server errors show the reference: they are the ones worth reporting,
    /// and the reference is how an administrator finds them in the log.
    /// </summary>
    public bool ShowRequestId => StatusCode >= 500 && !string.IsNullOrEmpty(RequestId);

    public static ErrorViewModel For(int statusCode, string? requestId) => statusCode switch
    {
        400 => new(statusCode,
            "That request could not be processed",
            "If you were submitting a form, it may have expired. Go back, reload the page and try again.",
            requestId),
        403 => new(statusCode,
            "You do not have access to this page",
            "Your account is not allowed to open it. If you think it should be, contact an administrator.",
            requestId),
        404 => new(statusCode,
            "Page not found",
            "The page does not exist, or is not available to your account. Check the address, or start again from your dashboard.",
            requestId),
        413 => new(statusCode,
            "That upload is too large",
            "The file is over the size limit shown on the form. Make it smaller and try again.",
            requestId),
        429 => new(statusCode,
            "Too many requests",
            "Wait a minute, then try again.",
            requestId),
        >= 500 => new(statusCode,
            "Something went wrong on our side",
            "The error has been logged. Try again in a moment; if it keeps happening, contact an administrator and quote the reference below.",
            requestId),
        _ => new(statusCode,
            "That request could not be completed",
            "Go back and try again, or start again from your dashboard.",
            requestId),
    };
}
