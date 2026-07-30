using System.Reflection;
using FluentAssertions;
using SmartPlagiarism.Engine;

namespace SmartPlagiarism.Tests;

/// <summary>
/// Guards the architecture rule in CLAUDE.md: the plagiarism engine is a
/// standalone library, so it must not depend on ASP.NET Core, EF Core, or any
/// other project in this solution. Breaking that rule fails the build here
/// rather than during a code review.
/// </summary>
public class EngineIsolationTests
{
    private static readonly string[] s_forbiddenAssemblyPrefixes =
    [
        "Microsoft.AspNetCore",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Extensions.Identity",
        "SmartPlagiarism.",
    ];

    [Fact]
    public void EngineAssembly_ReferencesNeitherWebNorPersistenceAssemblies()
    {
        var referencedAssemblies = typeof(EngineInfo).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty)
            .ToArray();

        referencedAssemblies.Should().NotContain(
            name => s_forbiddenAssemblyPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)),
            "the plagiarism engine must stay independent of ASP.NET Core, EF Core and the other projects");
    }
}
