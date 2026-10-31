using System.Net;
using FluentAssertions;
using SmartPlagiarism.Core.Identity;

namespace SmartPlagiarism.Tests.Web;

/// <summary>
/// The page frame through the real app: who gets the sidebar, who gets the top bar,
/// what each role's menu offers, and that the fonts are served from this site.
/// </summary>
public sealed class LayoutTests(SignInTestFactory factory) : IClassFixture<SignInTestFactory>
{
    [Fact]
    public async Task ASignedOutVisitor_GetsTheTopBar_NotTheSidebar()
    {
        var html = await factory.CreateClient().GetStringAsync("/");

        html.Should().Contain("navbar").And.Contain("/Account/Login").And.NotContain("app-sidebar");
    }

    [Fact]
    public async Task TheSignInPage_IsASplitScreen_WithNoOtherFrame()
    {
        var html = await factory.CreateClient().GetStringAsync("/Account/Login");

        html.Should().Contain("auth-split").And.NotContain("navbar").And.NotContain("app-sidebar");
    }

    [Theory]
    [InlineData(ApplicationRoles.Student, new[] { "/Dashboard/Student", "/Submissions", "/Submissions/Create" }, new[] { "/Review", "/Users", "/AuditLog" })]
    [InlineData(ApplicationRoles.Teacher, new[] { "/Dashboard/Teacher", "/Review" }, new[] { "/Submissions/Create", "/Users", "/AuditLog" })]
    [InlineData(ApplicationRoles.Admin, new[] { "/Dashboard/Admin", "/Statistics", "/Users", "/Departments", "/Courses", "/Semesters", "/Archive", "/ApiKeys", "/AuditLog" }, new[] { "/Submissions/Create", "/Review" })]
    public async Task ASignedInUser_GetsTheSidebar_WithTheirRolesMenuOnly(string role, string[] offered, string[] notOffered)
    {
        var email = $"layout.{role.ToLowerInvariant()}@uni.edu";
        await factory.CreateAccountAsync(email, role);
        var client = factory.NoRedirectClient();
        var signIn = await client.PostAsync("/Account/Login", SignInTestFactory.LoginForm(email, SignInTestFactory.Password, await client.GetStringAsync("/Account/Login")));
        signIn.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var html = await client.GetStringAsync("/Home/About");

        html.Should().Contain("app-sidebar").And.Contain("app-mobilebar").And.Contain(email).And.Contain("/Account/Logout")
            .And.NotContain("navbar", "signed-in pages use the sidebar, not the top bar");
        offered.Should().AllSatisfy(path => html.Should().Contain($"href=\"{path}\""));
        notOffered.Should().AllSatisfy(path => html.Should().NotContain($"href=\"{path}\""));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Account/Login")]
    public async Task ThePageFrame_HasNoInlineStyles_WhichThePolicyWouldRefuse(string path)
    {
        var html = await factory.CreateClient().GetStringAsync(path);

        html.Should().NotContain(" style=\"").And.NotContain("<style");
    }

    [Theory]
    [InlineData("/fonts/inter-latin-wght-normal.woff2")]
    [InlineData("/fonts/source-serif-4-latin-wght-normal.woff2")]
    [InlineData("/fonts/noto-sans-bengali-bengali-wght-normal.woff2")]
    public async Task TheFontsTheThemeNames_AreServedFromThisSite(string path)
    {
        var client = factory.CreateClient();

        var font = await client.GetAsync(path);
        var theme = await client.GetStringAsync("/css/theme.css");

        font.StatusCode.Should().Be(HttpStatusCode.OK);
        theme.Should().Contain($"url(\"..{path}\")");
    }
}
