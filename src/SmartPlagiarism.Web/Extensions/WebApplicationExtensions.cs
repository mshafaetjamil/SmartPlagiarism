using System.Data.Common;
using SmartPlagiarism.Infrastructure.Data;

namespace SmartPlagiarism.Web.Extensions;

public static class WebApplicationExtensions
{
    /// <summary>
    /// Applies migrations and seeds the demo roles and accounts. Development only.
    /// </summary>
    public static async Task SeedDevelopmentDataAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            await DbSeeder.SeedAsync(scope.ServiceProvider, app.Lifetime.ApplicationStopping);
        }
        catch (DbException ex)
        {
            // Only database-reachability problems are tolerated, and only here in
            // Development: a developer working on views should not need SQL Server
            // running. Seeding failures for any other reason (bad password policy,
            // duplicate role, ...) still bring the app down loudly.
            logger.LogWarning(
                ex,
                "Skipped Development seeding: the database could not be reached or is missing its schema. "
                + "Start SQL Server (see README) and restart to seed the demo accounts.");
        }
    }
}
