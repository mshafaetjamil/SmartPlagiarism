using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartPlagiarism.Core.Auditing;
using SmartPlagiarism.Infrastructure.Admin;
using SmartPlagiarism.Infrastructure.Data;
using SmartPlagiarism.Infrastructure.Services;
using SmartPlagiarism.Tests.Analysis;

namespace SmartPlagiarism.Tests.Api;

/// <summary>API keys: stored only as hashes, shown once, revocable.</summary>
public class ApiKeyServiceTests
{
    private static ApiKeyService NewService(AppDbContext context) =>
        new(context, new AuditService(context, NullLogger<AuditService>.Instance));

    [Fact]
    public async Task ANewKey_IsStoredOnlyAsAHash_AndAuthenticates()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        await using var context = harness.NewContext();
        var service = NewService(context);

        var (result, created) = await service.CreateAsync("Student portal", "admin-id");

        result.Succeeded.Should().BeTrue();
        created!.Key.Should().StartWith("spk_").And.HaveLength(12 + 1 + 43);

        var stored = await context.ApiKeys.SingleAsync();
        stored.Prefix.Should().Be(created.Key[..12]);
        stored.KeyHash.Should().HaveLength(64).And.NotContain(created.Key[13..], "only a hash of the secret part may be stored");

        (await service.AuthenticateAsync(created.Key))!.Name.Should().Be("Student portal");
        (await context.AuditLogs.SingleAsync()).Action.Should().Be(AuditActions.ApiKeyCreated);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer something")]
    [InlineData("spk_")]
    public async Task MalformedKeys_DoNotAuthenticate(string? presented)
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        await using var context = harness.NewContext();

        (await NewService(context).AuthenticateAsync(presented)).Should().BeNull();
    }

    [Fact]
    public async Task ARightPrefixWithAWrongSecret_DoesNotAuthenticate()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        await using var context = harness.NewContext();
        var service = NewService(context);
        var (_, created) = await service.CreateAsync("Student portal", "admin-id");

        var tampered = created!.Key[..^1] + (created.Key[^1] == 'A' ? 'B' : 'A');

        (await service.AuthenticateAsync(tampered)).Should().BeNull();
    }

    [Fact]
    public async Task ARevokedKey_NoLongerAuthenticates()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        await using var context = harness.NewContext();
        var service = NewService(context);
        var (_, created) = await service.CreateAsync("Student portal", "admin-id");

        (await service.RevokeAsync(created!.Id, "admin-id")).Succeeded.Should().BeTrue();

        (await service.AuthenticateAsync(created.Key)).Should().BeNull();
        (await service.GetKeysAsync()).Single().RevokedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task AKeyNeedsAName()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        await using var context = harness.NewContext();

        var (result, created) = await NewService(context).CreateAsync("   ", "admin-id");

        result.Succeeded.Should().BeFalse();
        created.Should().BeNull();
    }
}
