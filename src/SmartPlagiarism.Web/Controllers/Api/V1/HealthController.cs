using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartPlagiarism.Web.Models.Api.V1;

namespace SmartPlagiarism.Web.Controllers.Api.V1;

/// <summary>
/// Liveness endpoint for the REST surface. Lets the university portal (and this
/// project's own smoke checks) confirm the API is reachable and see which engine
/// build is serving requests.
/// </summary>
[Route("api/v1/health")]
[AllowAnonymous]
public class HealthController : ApiControllerBase
{
    /// <summary>GET /api/v1/health</summary>
    [HttpGet]
    public ActionResult<HealthResponse> Get()
    {
        return Ok(HealthResponse.Healthy());
    }
}
