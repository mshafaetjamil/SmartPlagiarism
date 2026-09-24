using Microsoft.AspNetCore.Mvc;

namespace SmartPlagiarism.Web.Controllers.Api.V1;

/// <summary>
/// Base class for the versioned REST surface that the university portal
/// integrates with: automatic model-state validation and problem-details error
/// responses.
///
/// Each controller declares its own <c>api/v1/...</c> route rather than taking
/// <c>[controller]</c> from its class name: the API's submissions controller
/// cannot also be called SubmissionsController without clashing with the web
/// UI's in link generation.
///
/// No blanket <c>[Produces("application/json")]</c>: it would also stamp error
/// responses, which should go out as <c>application/problem+json</c>. JSON is the
/// default for everything else anyway.
/// </summary>
[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
}
