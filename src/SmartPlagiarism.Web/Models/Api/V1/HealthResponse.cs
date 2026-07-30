using SmartPlagiarism.Engine;

namespace SmartPlagiarism.Web.Models.Api.V1;

/// <summary>Payload returned by <c>GET /api/v1/health</c>.</summary>
/// <param name="Status">Always "Healthy" when the API responds.</param>
/// <param name="EngineVersion">Version of the plagiarism engine in this build.</param>
/// <param name="ServerTimeUtc">Server clock, so callers can spot clock skew.</param>
public record HealthResponse(string Status, string EngineVersion, DateTime ServerTimeUtc)
{
    public static HealthResponse Healthy() =>
        new("Healthy", EngineInfo.Version, DateTime.UtcNow);
}
