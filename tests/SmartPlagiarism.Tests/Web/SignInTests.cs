using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SmartPlagiarism.Core.Identity;
using SmartPlagiarism.Tests.Api;

namespace SmartPlagiarism.Tests.Web;

/// <summary>
/// The sign-in page through the real app: it never says whether an account
/// exists, and it stops an address that keeps failing.
/// </summary>
public sealed class SignInTests(SignInTestFactory factory) : IClassFixture<SignInTestFactory>
{
    [Fact]
    public async Task ARightPassword_SignsIn()
    {
        const string email = "signin.right@uni.edu";
        await factory.CreateAccountAsync(email);

        var response = await factory.SignInAsync(email, SignInTestFactory.Password);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().StartWith("/Dashboard");
    }

    [Fact]
    public async Task OutsideDevelopment_CookiesAreHttpsOnly_EvenWhenTheRequestCameOverPlainHttp()
    {
        // As behind a proxy that ends TLS, so the app itself is reached over HTTP.
        var plain = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost") });

        var form = await plain.GetAsync("/Account/Login");

        form.Headers.GetValues("Set-Cookie").Should().Contain(cookie => cookie.StartsWith(".AspNetCore.Antiforgery."))
            .And.AllSatisfy(cookie => cookie.Should().ContainEquivalentOf("; secure"));

        // The sign-in cookie too - signed in over HTTPS, since the form needs its
        // now HTTPS-only antiforgery cookie sent back.
        const string email = "signin.cookies@uni.edu";
        await factory.CreateAccountAsync(email);

        var signIn = await factory.SignInAsync(email, SignInTestFactory.Password);

        signIn.Headers.GetValues("Set-Cookie").Should().Contain(cookie => cookie.StartsWith(".AspNetCore.Identity.Application="))
            .And.AllSatisfy(cookie => cookie.Should().ContainEquivalentOf("; secure"));
    }

    [Fact]
    public async Task AnUnknownEmail_AWrongPassword_AndALockedAccount_AllGetTheSameAnswer()
    {
        const string email = "signin.locked@uni.edu";
        await factory.CreateAccountAsync(email);

        var answers = new List<string> { await factory.SignInFailureAsync("nobody@uni.edu", SignInTestFactory.Password) };

        // The fifth wrong password locks the account.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            answers.Add(await factory.SignInFailureAsync(email, "Wrong#Password1"));
        }

        // Locked now, so even the right password is refused - with the same words.
        answers.Add(await factory.SignInFailureAsync(email, SignInTestFactory.Password));

        answers.Should().AllSatisfy(answer => answer.Should()
            .Contain("Invalid email or password. Repeated failures lock an account for a few minutes.")
            .And.NotContainEquivalentOf("locked out"));
    }
}

/// <summary>The failed sign-in limit per address, on its own app instance so no other test adds to the count.</summary>
public sealed class SignInThrottlingTests(ThrottledSignInTestFactory factory) : IClassFixture<ThrottledSignInTestFactory>
{
    [Fact]
    public async Task AfterTooManyFailuresFromOneAddress_EvenTheRightPasswordIsRefused()
    {
        const string email = "signin.throttled@uni.edu";
        await factory.CreateAccountAsync(email);

        for (var attempt = 0; attempt < ThrottledSignInTestFactory.Limit; attempt++)
        {
            await factory.SignInFailureAsync($"guess{attempt}@uni.edu", "Guess#Password1");
        }

        var refused = await factory.SignInFailureAsync(email, SignInTestFactory.Password);

        refused.Should().Contain("Too many failed sign-in attempts from this network");
    }
}

/// <summary>The real app with accounts to sign in as. Redirects are not followed, so a sign-in shows as one.</summary>
public class SignInTestFactory : ApiTestFactory
{
    public const string Password = "Student#2026-check";

    public async Task CreateAccountAsync(string email, string role = ApplicationRoles.Student)
    {
        await SeedAsync();

        await using var scope = Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email, FullName = "Sign-in test student", EmailConfirmed = true };

        (await users.CreateAsync(user, Password)).Succeeded.Should().BeTrue();
        await users.AddToRoleAsync(user, role);
    }

    /// <summary>Posts the real login form, antiforgery token and all.</summary>
    public async Task<HttpResponseMessage> SignInAsync(string email, string password)
    {
        var client = NoRedirectClient();
        var form = await client.GetStringAsync("/Account/Login");

        return await client.PostAsync("/Account/Login", LoginForm(email, password, form));
    }

    public HttpClient NoRedirectClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = ClientOptions.BaseAddress });

    /// <param name="loginPage">The page the form came from, for its antiforgery token.</param>
    public static FormUrlEncodedContent LoginForm(string email, string password, string loginPage) =>
        new(new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = HardeningTestFactory.AntiforgeryToken(loginPage),
        });

    /// <summary>The page shown for a refused sign-in.</summary>
    public async Task<string> SignInFailureAsync(string email, string password)
    {
        var response = await SignInAsync(email, password);

        response.StatusCode.Should().Be(HttpStatusCode.OK, "a refused sign-in shows the form again");
        return await response.Content.ReadAsStringAsync();
    }
}

/// <summary>The same app allowing each address only a few failed sign-ins.</summary>
public sealed class ThrottledSignInTestFactory : SignInTestFactory
{
    public const int Limit = 3;

    protected override int SignInFailuresPerAddress => Limit;
}
