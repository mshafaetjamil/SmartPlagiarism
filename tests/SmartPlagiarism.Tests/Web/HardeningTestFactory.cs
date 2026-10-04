using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartPlagiarism.Core.Entities;
using SmartPlagiarism.Core.Identity;
using SmartPlagiarism.Infrastructure.Data;
using SmartPlagiarism.Tests.Api;

namespace SmartPlagiarism.Tests.Web;

/// <summary>
/// The real app, plus <see cref="ThrowingController"/> to break on purpose and
/// a teacher and an admin to sign in as.
/// </summary>
public sealed partial class HardeningTestFactory : ApiTestFactory
{
    public const string TeacherEmail = "hardening.teacher@uni.edu";

    public const string AdminEmail = "hardening.admin@uni.edu";

    private const string StaffPassword = "Staff#2026-check";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
            services.AddControllers().AddApplicationPart(typeof(ThrowingController).Assembly));
    }

    /// <summary>A client signed in through the real login form, antiforgery token and all.</summary>
    public async Task<HttpClient> SignedInClientAsync(string email)
    {
        await SeedStaffAsync();

        var client = CreateClient();
        var form = await client.GetStringAsync("/Account/Login");

        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = StaffPassword,
            ["__RequestVerificationToken"] = AntiforgeryToken(form),
        }));

        response.EnsureSuccessStatusCode();
        response.RequestMessage!.RequestUri!.AbsolutePath.Should().StartWith("/Dashboard", "the sign-in should succeed");

        return client;
    }

    /// <summary>The token a form rendered by the app carries.</summary>
    public static string AntiforgeryToken(string html) => TokenPattern().Match(html).Groups[1].Value;

    private async Task SeedStaffAsync()
    {
        await SeedAsync();

        await using var scope = Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (await users.FindByEmailAsync(TeacherEmail) is not null)
        {
            return;
        }

        foreach (var (email, role) in new[] { (TeacherEmail, ApplicationRoles.Teacher), (AdminEmail, ApplicationRoles.Admin) })
        {
            var user = new ApplicationUser { UserName = email, Email = email, FullName = role + " for the hardening tests", EmailConfirmed = true };
            (await users.CreateAsync(user, StaffPassword)).Succeeded.Should().BeTrue();
            await users.AddToRoleAsync(user, role);

            if (role == ApplicationRoles.Teacher)
            {
                context.TeacherProfiles.Add(new TeacherProfile
                {
                    UserId = user.Id,
                    EmployeeNumber = "T-HARDENING",
                    DepartmentId = await context.Departments.Select(department => department.Id).FirstAsync(),
                });
                await context.SaveChangesAsync();
            }
        }
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();
}

/// <summary>
/// Throws from inside an action, as a bug in a service would, to see what
/// reaches the user and what reaches the log.
/// </summary>
[AllowAnonymous]
public sealed class ThrowingController : Controller
{
    public const string PagePath = "/test/throw";

    public const string ApiPath = "/api/test/throw";

    /// <summary>Stands in for what an exception message can carry: SQL, paths, data.</summary>
    public const string Secret = "internal detail: Server=db01;Password=hunter2";

    [HttpGet(PagePath)]
    [HttpGet(ApiPath)]
    public IActionResult Throw() => throw new InvalidOperationException(Secret);
}
