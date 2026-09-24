using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using SmartPlagiarism.Web.Authorization;
using SmartPlagiarism.Web.Models.Api.V1;

namespace SmartPlagiarism.Web.Extensions;

/// <summary>
/// Everything the /api/v1 surface needs beyond the controllers themselves:
/// API-key authentication, the upload rate limit, and the OpenAPI document.
/// </summary>
public static class RestApiExtensions
{
    public static IServiceCollection AddRestApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ApiOptions>()
            .Bind(configuration.GetSection(ApiOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // A second scheme beside Identity's cookie; only the ApiClient policy
        // asks for it, so the web UI is unaffected.
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, configureOptions: null);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Per key, so one busy integration cannot use up another's allowance.
            options.AddPolicy(ApiRateLimits.Uploads, context =>
            {
                var limit = context.RequestServices.GetRequiredService<IOptions<ApiOptions>>().Value.UploadsPerMinute;
                var partition = context.User.FindFirstValue(ApiKeyAuthenticationHandler.ApiKeyIdClaim) ?? "anonymous";

                return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limit,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                });
            });

            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                return ValueTask.CompletedTask;
            };
        });

        services.AddOpenApi("v1", options =>
        {
            options.ShouldInclude = description =>
                description.RelativePath?.StartsWith("api/", StringComparison.Ordinal) == true;

            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "Smart Plagiarism API";
                document.Info.Description =
                    $"Send the key an administrator issued in the `{ApiKeyAuthenticationHandler.HeaderName}` header.";

                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[ApiKeyAuthenticationHandler.SchemeName] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    In = ParameterLocation.Header,
                    Name = ApiKeyAuthenticationHandler.HeaderName,
                    Description = "An API key issued under Admin > API keys.",
                };

                document.Security =
                [
                    new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference(ApiKeyAuthenticationHandler.SchemeName, document)] = [],
                    },
                ];

                return Task.CompletedTask;
            });
        });

        return services;
    }
}
