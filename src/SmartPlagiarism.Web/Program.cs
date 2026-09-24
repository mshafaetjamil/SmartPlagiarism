using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using SmartPlagiarism.Core.Identity;
using SmartPlagiarism.Infrastructure;
using SmartPlagiarism.Web.Authorization;
using SmartPlagiarism.Web.Extensions;

var builder = WebApplication.CreateBuilder(args);

// EF Core, Identity and the rest of the persistence stack.
builder.Services.AddInfrastructure(builder.Configuration);

// Identity's cookie defaults already point at /Account/*, but pinning them here
// keeps the routes and the auth configuration visible in one place.
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthorizationPolicies.AdminOnly, policy => policy.RequireRole(ApplicationRoles.Admin));
    options.AddPolicy(AuthorizationPolicies.TeacherOnly, policy => policy.RequireRole(ApplicationRoles.Teacher));
    options.AddPolicy(AuthorizationPolicies.StudentOnly, policy => policy.RequireRole(ApplicationRoles.Student));
    options.AddPolicy(
        AuthorizationPolicies.CanViewSubmission,
        policy => policy.RequireAuthenticatedUser().AddRequirements(new SubmissionAccessRequirement()));
    options.AddPolicy(
        AuthorizationPolicies.ApiClient,
        policy => policy
            .AddAuthenticationSchemes(ApiKeyAuthenticationHandler.SchemeName)
            .RequireAuthenticatedUser()
            .RequireClaim(ApiKeyAuthenticationHandler.ApiKeyIdClaim));
});

// Scoped: it asks the database, through the request's DbContext.
builder.Services.AddScoped<IAuthorizationHandler, SubmissionAccessHandler>();

// API-key authentication, the upload rate limit and the OpenAPI document.
builder.Services.AddRestApi(builder.Configuration);

// Covers both the MVC views and the attribute-routed /api/v1 controllers. Enums
// travel as their names, so API clients read "Completed" rather than 2.
builder.Services.AddControllersWithViews()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await app.SeedDevelopmentDataAsync();

    // The API's OpenAPI document and a Swagger UI over it, Development only.
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Smart Plagiarism API v1"));
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for
    // production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// After authorization: an API call's key is only authenticated there, and the
// upload limit is counted per key.
app.UseRateLimiter();

app.MapStaticAssets();

app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// Attribute-routed REST endpoints under /api/v1.
app.MapControllers();

app.Run();

/// <summary>Exposed so integration tests can host the app with WebApplicationFactory.</summary>
public partial class Program;
