using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartPlagiarism.Core.Files;
using SmartPlagiarism.Infrastructure.Storage;

namespace SmartPlagiarism.Tests.Storage;

/// <summary>
/// Exercises the real service against a real temporary directory - the layout,
/// the hash and the deduplication rule are all things a mock would let us get
/// wrong without noticing.
/// </summary>
public class LocalFileStorageServiceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "smartplagiarism-tests", Guid.NewGuid().ToString("N"));

    private LocalFileStorageService CreateService(string? root = null) =>
        new(Options.Create(new FileStorageOptions { RootPath = root ?? _root }),
            new FileUploadValidator(),
            NullLogger<LocalFileStorageService>.Instance);

    private static MemoryStream Pdf(string body) => new(Encoding.Latin1.GetBytes("%PDF-1.7\n" + body));

    private static string Sha256Of(Stream stream)
    {
        stream.Seek(0, SeekOrigin.Begin);
        var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
        stream.Seek(0, SeekOrigin.Begin);
        return hash;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task StoreAsync_WritesToShardedHashNamedPath()
    {
        var service = CreateService();
        using var content = Pdf("a report");
        var expectedHash = Sha256Of(content);

        var stored = await service.StoreAsync(new FileUpload("Report Final(2).pdf", content.Length, content));

        stored.Sha256Hash.Should().Be(expectedHash);
        stored.StoragePath.Should().Be($"{expectedHash[..2]}/{expectedHash}.pdf");
        File.Exists(Path.Combine(_root, expectedHash[..2], expectedHash + ".pdf")).Should().BeTrue();
    }

    [Fact]
    public async Task StoreAsync_NeverUsesTheClientFileName()
    {
        var service = CreateService();
        using var content = Pdf("a report");

        var stored = await service.StoreAsync(new FileUpload("../../escape attempt.pdf", content.Length, content));

        var everyStoredFile = Directory.GetFiles(_root, "*", SearchOption.AllDirectories);

        everyStoredFile.Should().ContainSingle();
        Path.GetFileName(everyStoredFile[0]).Should().Be(stored.Sha256Hash + ".pdf");
        everyStoredFile[0].Should().NotContain("escape");
    }

    [Fact]
    public async Task StoreAsync_WrittenBytesMatchTheUpload()
    {
        var service = CreateService();
        using var content = Pdf("exact bytes matter");

        var stored = await service.StoreAsync(new FileUpload("report.pdf", content.Length, content));

        var written = await File.ReadAllBytesAsync(Path.Combine(_root, stored.StoragePath));
        written.Should().Equal(content.ToArray());
    }

    [Fact]
    public async Task StoreAsync_IdenticalContentTwice_IsDeduplicated()
    {
        var service = CreateService();
        using var first = Pdf("identical contents");
        using var second = Pdf("identical contents");

        var firstStored = await service.StoreAsync(new FileUpload("original.pdf", first.Length, first));
        var writtenAt = File.GetLastWriteTimeUtc(Path.Combine(_root, firstStored.StoragePath));

        var secondStored = await service.StoreAsync(new FileUpload("a-copy.pdf", second.Length, second));

        secondStored.Sha256Hash.Should().Be(firstStored.Sha256Hash);
        secondStored.StoragePath.Should().Be(firstStored.StoragePath);
        secondStored.WasDeduplicated.Should().BeTrue();
        firstStored.WasDeduplicated.Should().BeFalse();

        Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
            .Should().ContainSingle("the same bytes must only ever occupy one file");

        File.GetLastWriteTimeUtc(Path.Combine(_root, firstStored.StoragePath))
            .Should().Be(writtenAt, "the existing file must be reused, not rewritten");
    }

    [Fact]
    public async Task StoreAsync_DifferentContent_GetsSeparateFiles()
    {
        var service = CreateService();
        using var first = Pdf("one");
        using var second = Pdf("two");

        var firstStored = await service.StoreAsync(new FileUpload("one.pdf", first.Length, first));
        var secondStored = await service.StoreAsync(new FileUpload("two.pdf", second.Length, second));

        secondStored.Sha256Hash.Should().NotBe(firstStored.Sha256Hash);
        Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Should().HaveCount(2);
    }

    [Fact]
    public async Task StoreAsync_RecordsTheCanonicalContentTypeNotTheClientClaim()
    {
        var service = CreateService();
        using var content = Pdf("a report");

        var stored = await service.StoreAsync(new FileUpload("report.pdf", content.Length, content));

        stored.ContentType.Should().Be("application/pdf");
    }

    [Fact]
    public async Task StoreAsync_InvalidFile_IsRefusedAndNothingIsWritten()
    {
        var service = CreateService();
        using var content = new MemoryStream([0x4D, 0x5A, 0x90, 0x00]);

        var act = () => service.StoreAsync(new FileUpload("malware.pdf", content.Length, content));

        await act.Should().ThrowAsync<InvalidOperationException>();
        Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task StoreAsync_LeavesNoTemporaryFilesBehind()
    {
        var service = CreateService();
        using var content = Pdf("a report");

        await service.StoreAsync(new FileUpload("report.pdf", content.Length, content));

        Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public void Constructor_RootInsideWwwroot_FailsFast()
    {
        var webRoot = Path.Combine(_root, "wwwroot", "uploads");

        var act = () => CreateService(webRoot);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*inside wwwroot*", "serving uploaded coursework as static files would be a data leak");
    }
}
