using Microsoft.AspNetCore.Mvc;

namespace SmartPlagiarism.Web.Controllers.Api.V1;

/// <summary>
/// Base class for the versioned REST surface that the university portal will
/// integrate with. Every derived controller is routed under <c>/api/v1</c> and
/// gets automatic model-state validation and problem-details error responses.
/// </summary>
[ApiController]
[Produces("application/json")]
[Route("api/v1/[controller]")]
public abstract class ApiControllerBase : ControllerBase
{
}
