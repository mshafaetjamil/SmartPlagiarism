using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartPlagiarism.Core.Auditing;
using SmartPlagiarism.Infrastructure.Data;

namespace SmartPlagiarism.Tests.Web;

/// <summary>
/// Behind a reverse proxy: the client's own address, from X-Forwarded-For, is
/// believed only when the request comes from a proxy listed in the settings.
/// The address shows in the audit log of a failed sign-in.
/// </summary>
public sealed class ReverseProxyTests(ProxiedSignInTestFactory factory) : IClassFixture<ProxiedSignInTestFactory>
{
    [Fact]
    public async Task ThroughATrustedProxy_TheClientsOwnAddressIsRecorded()
    {
        var details = await factory.FailedSignInDetailsAsync(
            connectionFrom: ProxiedSignInTestFactory.TrustedProxy,
            forwardedFor: "203.0.113.9",
            email: "via.trusted.proxy@uni.edu");

        details.Should().Contain("IP: 203.0.113.9");
    }

    [Fact]
    public async Task FromAnyoneElse_AForwardedAddressIsIgnored()
    {
        var details = await factory.FailedSignInDetailsAsync(
            connectionFrom: "10.0.0.6",
            forwardedFor: "203.0.113.10",
            email: "via.unknown.proxy@uni.edu");

        details.Should().Contain("IP: 10.0.0.6").And.NotContain("203.0.113.10");
    }
}

/// <summary>
/// The app with 10.0.0.5 listed as its reverse proxy. The in-memory test server
/// gives a request no remote address, so a test header supplies the address the
/// connection would have come from.
/// </summary>
public sealed class ProxiedSignInTestFactory : SignInTestFactory
{
    public const string TrustedProxy = "10.0.0.5";

    private const string ConnectionFromHeader = "X-Test-Connection-From";

    /// <summary>Fails a sign-in as seen from <paramref name="connectionFrom"/>, and returns what the audit log recorded.</summary>
    public async Task<string> FailedSignInDetailsAsync(string connectionFrom, string forwardedFor, string email)
    {
        await SeedAsync();

        var client = NoRedirectClient();
        client.DefaultRequestHeaders.Add(ConnectionFromHeader, connectionFrom);
        client.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedFor);

        var page = await client.GetStringAsync("/Account/Login");
        var response = await client.PostAsync("/Account/Login", LoginForm(email, "Wrong#Password1", page));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = Services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogs
            .Where(entry => entry.Action == AuditActions.LoginFailed && entry.Details!.Contains(email))
            .Select(entry => entry.Details!)
            .SingleAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.UseSetting("ReverseProxy:KnownProxies:0", TrustedProxy);
        builder.ConfigureTestServices(services => services.AddSingleton<IStartupFilter, ConnectionFromTestHeader>());
    }

    /// <summary>Runs ahead of the app's own pipeline, as the connection itself would.</summary>
    private sealed class ConnectionFromTestHeader : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (context.Request.Headers.TryGetValue(ConnectionFromHeader, out var address))
                {
                    context.Connection.RemoteIpAddress = IPAddress.Parse(address.ToString());
                }

                return nextMiddleware(context);
            });

            next(app);
        };
    }
}
