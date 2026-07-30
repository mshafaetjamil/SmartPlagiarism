using SmartPlagiarism.Core.Identity;
using SmartPlagiarism.Infrastructure;
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
});

// Covers both the MVC views and the attribute-routed /api/v1 controllers.
builder.Services.AddControllersWithViews();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await app.SeedDevelopmentDataAsync();
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

app.MapStaticAssets();

app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// Attribute-routed REST endpoints under /api/v1.
app.MapControllers();

app.Run();
