using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using SmartPlagiarism.Web.Controllers;
using SmartPlagiarism.Web.Controllers.Api.V1;
using SmartPlagiarism.Web.Security;

namespace SmartPlagiarism.Tests.Web;

/// <summary>
/// The hardening, checked against the real app over HTTP: what a user sees
/// when something fails, what the log keeps, the security headers, and the
/// antiforgery check on every form post.
/// </summary>
public sealed partial class HardeningTests(HardeningTestFactory factory) : IClassFixture<HardeningTestFactory>
{
    [Fact]
    public async Task AnUnknownPage_GetsTheFriendlyNotFoundPage()
    {
        var response = await factory.CreateClient().GetAsync("/no/such/page");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        (await response.Content.ReadAsStringAsync()).Should().Contain("Page not found").And.Contain("navbar", "it is a page of the site, in its layout");
    }

    [Fact]
    public async Task AnUnknownApiAddress_GetsProblemDetails()
    {
        var response = await factory.CreateClient().GetAsync("/api/v1/no-such-endpoint");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var problem = await JsonAsync(response);
        problem.GetProperty("status").GetInt32().Should().Be(404);
        problem.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task AnUnhandledException_ShowsTheErrorPageWithAReference_WhichFindsTheDetailsInTheLog()
    {
        var response = await factory.CreateClient().GetAsync(ThrowingController.PagePath);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Something went wrong on our side");
        html.Should().NotContain("hunter2", "nothing from the exception may reach the browser");

        var reference = ReferencePattern().Match(html).Groups[1].Value;
        reference.Should().NotBeEmpty("the page gives a reference to quote");

        var logged = await factory.WaitForLogEventAsync(logEvent =>
            logEvent.TryGetProperty("@tr", out var traceId) && traceId.GetString() == reference
            && logEvent.TryGetProperty("@x", out _));

        logged.GetProperty("@l").GetString().Should().Be("Error");
        logged.GetProperty("@x").GetString().Should().Contain(ThrowingController.Secret, "the log keeps what the page leaves out");
        logged.GetProperty("RequestPath").GetString().Should().Be(ThrowingController.PagePath);
    }

    [Fact]
    public async Task AnUnhandledExceptionInTheApi_GetsProblemDetails_WithoutTheMessage()
    {
        var response = await factory.CreateClient().GetAsync(ThrowingController.ApiPath);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("hunter2");
        JsonDocument.Parse(body).RootElement.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task EachRequest_IsLoggedAsOneStructuredEvent()
    {
        (await factory.CreateClient().GetAsync("/Home/About")).EnsureSuccessStatusCode();

        var logged = await factory.WaitForLogEventAsync(logEvent =>
            logEvent.TryGetProperty("SourceContext", out var source) && source.GetString() == "Serilog.AspNetCore.RequestLoggingMiddleware"
            && logEvent.TryGetProperty("RequestPath", out var path) && path.GetString() == "/Home/About");

        logged.GetProperty("RequestMethod").GetString().Should().Be("GET");
        logged.GetProperty("StatusCode").GetInt32().Should().Be(200);
        logged.GetProperty("Elapsed").GetDouble().Should().BePositive();
        logged.GetProperty("Application").GetString().Should().Be("SmartPlagiarism");
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/no/such/page")]
    [InlineData("/api/v1/health")]
    [InlineData(ThrowingController.PagePath)]
    public async Task EveryResponse_CarriesTheSecurityHeaders(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);

        Header(response, "Content-Security-Policy").Should().Be(SecurityHeadersMiddleware.ContentSecurityPolicy);
        Header(response, "X-Content-Type-Options").Should().Be("nosniff");
        Header(response, "X-Frame-Options").Should().Be("DENY");
        Header(response, "Referrer-Policy").Should().Be("same-origin");
        Header(response, "Cross-Origin-Opener-Policy").Should().Be("same-origin");
        Header(response, "Permissions-Policy").Should().Contain("camera=()");
    }

    [Fact]
    public async Task AFormPostWithoutTheAntiforgeryToken_IsRefused()
    {
        await factory.SeedAsync();

        var response = await factory.CreateClient().PostAsync("/Account/Login", LoginForm(token: null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("it may have expired");
    }

    [Fact]
    public async Task AFormPostWithTheToken_IsAccepted()
    {
        await factory.SeedAsync();
        var client = factory.CreateClient();

        var form = await client.GetStringAsync("/Account/Login");
        var token = HardeningTestFactory.AntiforgeryToken(form);

        var response = await client.PostAsync("/Account/Login", LoginForm(token));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Invalid email or password.");
    }

    /// <summary>
    /// The Content-Security-Policy refuses inline styles, scripts and event
    /// handlers, so a page carrying one is broken in the browser - silently,
    /// unless someone reads the console. Each role's pages are checked here.
    /// </summary>
    [Theory]
    [InlineData(null, "/")]
    [InlineData(null, "/Account/Login")]
    [InlineData(null, "/no/such/page")]
    [InlineData(HardeningTestFactory.TeacherEmail, "/Dashboard/Teacher")]
    [InlineData(HardeningTestFactory.TeacherEmail, "/Review")]
    [InlineData(HardeningTestFactory.TeacherEmail, "/Review?MinSimilarity=500")]
    [InlineData(HardeningTestFactory.AdminEmail, "/Dashboard/Admin")]
    [InlineData(HardeningTestFactory.AdminEmail, "/Users")]
    [InlineData(HardeningTestFactory.AdminEmail, "/Users/Create")]
    [InlineData(HardeningTestFactory.AdminEmail, "/Departments")]
    [InlineData(HardeningTestFactory.AdminEmail, "/Courses/Create")]
    [InlineData(HardeningTestFactory.AdminEmail, "/Archive")]
    [InlineData(HardeningTestFactory.AdminEmail, "/Statistics")]
    [InlineData(HardeningTestFactory.AdminEmail, "/AuditLog")]
    [InlineData(HardeningTestFactory.AdminEmail, "/ApiKeys")]
    public async Task Pages_CarryNoInlineStyleOrScript_ForTheContentSecurityPolicyToRefuse(string? email, string path)
    {
        var client = email is null ? factory.CreateClient() : await factory.SignedInClientAsync(email);

        var html = await (await client.GetAsync(path)).Content.ReadAsStringAsync();

        InlineMarkupPattern().Matches(html).Select(match => match.Value).Should().BeEmpty();
    }

    /// <summary>
    /// Every action a browser form can post to is covered by an antiforgery
    /// check - the global filter, or its own attribute - and only the two
    /// deliberate exemptions are not.
    /// </summary>
    [Fact]
    public void EveryActionThatAcceptsAPost_ChecksTheAntiforgeryToken()
    {
        var actions = factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>()
            .ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .Where(AcceptsUnsafeMethods)
            .ToList();

        actions.Should().Contain(action => action.ControllerTypeInfo == typeof(SubmissionsController), "the check must see the real actions");

        var unprotected = actions
            .Where(action => EffectivePolicy(action) is not (AutoValidateAntiforgeryTokenAttribute or ValidateAntiForgeryTokenAttribute))
            .Select(action => action.ControllerTypeInfo.AsType())
            .Distinct();

        // The API (header keys, never cookies) and the error page (re-executed
        // for any failed request) opt out on purpose.
        unprotected.Should().OnlyContain(type =>
            typeof(ApiControllerBase).IsAssignableFrom(type) || type == typeof(ErrorController));
    }

    private static bool AcceptsUnsafeMethods(ControllerActionDescriptor action)
    {
        var methods = action.ActionConstraints?.OfType<HttpMethodActionConstraint>().SelectMany(constraint => constraint.HttpMethods).ToList();

        // No method attribute at all means every method.
        return methods is null || methods.Count == 0 || methods.Any(method => method is "POST" or "PUT" or "PATCH" or "DELETE");
    }

    /// <summary>
    /// MVC's rule: of the antiforgery attributes that apply - global, controller,
    /// action - the most specific wins. The two validating attributes are filter
    /// factories, so they are matched by type rather than as policies.
    /// </summary>
    private static IFilterMetadata? EffectivePolicy(ControllerActionDescriptor action) =>
        action.FilterDescriptors
            .Where(descriptor => descriptor.Filter
                is AutoValidateAntiforgeryTokenAttribute
                or ValidateAntiForgeryTokenAttribute
                or IAntiforgeryPolicy)
            .OrderBy(descriptor => descriptor.Order)
            .ThenBy(descriptor => descriptor.Scope)
            .Select(descriptor => descriptor.Filter)
            .LastOrDefault();

    private static FormUrlEncodedContent LoginForm(string? token)
    {
        var fields = new Dictionary<string, string> { ["Email"] = "nobody@uni.edu", ["Password"] = "Wrong#2026" };

        if (token is not null)
        {
            fields["__RequestVerificationToken"] = token;
        }

        return new FormUrlEncodedContent(fields);
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values)
            ? string.Join(",", values)
            : null;

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    [GeneratedRegex("Reference: <code>([^<]+)</code>")]
    private static partial Regex ReferencePattern();

    [GeneratedRegex(@"\sstyle\s*=|<style[\s>]|<script(?![^>]*\ssrc=)[^>]*>|\son[a-z]+\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex InlineMarkupPattern();
}
