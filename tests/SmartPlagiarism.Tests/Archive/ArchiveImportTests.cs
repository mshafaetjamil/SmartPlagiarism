using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.DTOs.Archive;
using SmartPlagiarism.Core.Entities;
using SmartPlagiarism.Core.Enums;
using SmartPlagiarism.Infrastructure.Reports;
using SmartPlagiarism.Infrastructure.Services;
using SmartPlagiarism.Tests.Analysis;
using SmartPlagiarism.Tests.Extraction;
using UglyToad.PdfPig;

namespace SmartPlagiarism.Tests.Archive;

/// <summary>
/// Importing past years' work from a ZIP, and new submissions being matched
/// against it - with the author shown to staff and hidden from students.
/// </summary>
public class ArchiveImportTests
{
    private const string ThesisParagraph =
        "Winnowing selects the minimum hash value within each sliding window of consecutive fingerprints, "
        + "which guarantees that any sufficiently long shared passage between two documents will be found "
        + "while storing only a small fraction of all the hashes that a document produces.";

    private const string OldReportParagraph =
        "The irrigation controller reads soil moisture every ten minutes and opens the valve only when the "
        + "reading falls below the threshold configured for the crop growing in that particular field.";

    private const string NewWork =
        "Our evaluation was carried out over two semesters with volunteers from the second year cohort, "
        + "who each submitted three short essays written under supervised conditions.";

    private static byte[] Zip(params (string Path, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();

        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                using var stream = zip.CreateEntry(path).Open();
                stream.Write(content);
            }
        }

        return buffer.ToArray();
    }

    private static byte[] Text(string text) => Encoding.UTF8.GetBytes(text);

    // ---------------- engine upgrades ----------------

    [Fact]
    public async Task ArchiveTextAnOlderEngineRead_IsReadAgainAtStartup()
    {
        // Nothing else ever re-reads an archive document, so an upgrade that
        // reads documents better must.
        await using var harness = await AnalysisTestHarness.CreateAsync();
        await harness.ImportArchiveAsync(Zip(("2015/thesis.docx", DocxFixture.Create([ThesisParagraph]))));

        await using (var context = harness.NewContext())
        {
            var text = await context.ExtractedTexts.SingleAsync();
            text.EngineVersion = "0.0.1";
            await context.SaveChangesAsync();
        }

        await using (var context = harness.NewContext())
        {
            await harness.NewAnalyzer(context).ResumePendingAsync();
        }

        await using var check = harness.NewContext();
        (await check.ExtractedTexts.SingleAsync()).EngineVersion.Should().Be(SmartPlagiarism.Engine.EngineInfo.Version);
    }

    // ---------------- importing ----------------

    [Fact]
    public async Task Import_DatesFilesByManifestThenYearFolder_AndSkipsWhatItCannotUse()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var oldReport = DocxFixture.Create([OldReportParagraph]);

        var importId = await harness.ImportArchiveAsync(Zip(
            ("manifest.csv", Text("file,title,author,year,department\n2015/reports/thesis.docx,\"Winnowing, Revisited\",Karim Ahmed,2016,CSE\n")),
            ("2012/a-report.docx", oldReport),
            ("2012/b-same-report.docx", oldReport),
            ("2015/reports/thesis.docx", DocxFixture.Create([ThesisParagraph])),
            ("undated.docx", DocxFixture.Create([NewWork])),
            ("2014/not-really.pdf", Text("this is not a PDF")),
            ("__MACOSX/2012/._a-report.docx", Text("resource fork")),
            ("2013/", [])));

        await using var context = harness.NewContext();
        var import = await context.ArchiveImports.SingleAsync(candidate => candidate.Id == importId);
        var documents = await context.ArchiveDocuments.AsNoTracking().Include(document => document.File).ThenInclude(file => file.Fingerprint)
            .OrderBy(document => document.Year).ToListAsync();

        import.Status.Should().Be(ArchiveImportStatus.Completed);
        import.TotalFiles.Should().Be(5, "the manifest, the macOS clutter and the folder entry are not documents");
        import.ImportedFiles.Should().Be(2);
        import.SkippedFiles.Should().Be(3);
        import.Messages.Should().Contain(message => message.StartsWith("2012/b-same-report.docx: already in the archive"));
        import.Messages.Should().Contain(message => message.StartsWith("undated.docx: no year"));
        import.Messages.Should().Contain(message => message.StartsWith("2014/not-really.pdf:"));
        import.PackagePath.Should().BeNull("the ZIP is deleted once imported");
        import.CompletedAtUtc.Should().NotBeNull();

        documents.Select(document => (document.Year, document.Title, document.Author)).Should().Equal(
            (2012, "a-report", null),
            (2016, "Winnowing, Revisited", "Karim Ahmed"));
        documents[1].DepartmentId.Should().Be(await harness.DepartmentIdAsync("CSE"));
        documents.Should().OnlyContain(document => document.File.Fingerprint != null && document.File.SubmissionId == null);
        documents.Should().OnlyContain(document => document.ArchiveImportId == importId);
    }

    [Fact]
    public async Task Import_UndatedFiles_TakeTheDefaultYearAndDepartment()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var cse = await harness.DepartmentIdAsync("CSE");

        await harness.ImportArchiveAsync(Zip(("Final_Year_Report.docx", DocxFixture.Create([OldReportParagraph]))), defaultYear: 2013, defaultDepartmentId: cse);

        await using var context = harness.NewContext();
        var document = await context.ArchiveDocuments.SingleAsync();

        document.Year.Should().Be(2013);
        document.DepartmentId.Should().Be(cse);
        document.Title.Should().Be("Final Year Report");
    }

    [Fact]
    public async Task Import_ManifestInsideTheZipsTopFolder_StillApplies()
    {
        // Compressing a folder puts everything under that folder's name.
        await using var harness = await AnalysisTestHarness.CreateAsync();

        await harness.ImportArchiveAsync(Zip(
            ("cse-archive/manifest.csv", Text("title,file,year\nSmart Farming,old.docx,2011\n")),
            ("cse-archive/old.docx", DocxFixture.Create([OldReportParagraph]))));

        await using var context = harness.NewContext();
        var document = await context.ArchiveDocuments.SingleAsync();

        document.Year.Should().Be(2011);
        document.Title.Should().Be("Smart Farming");
    }

    [Fact]
    public async Task Import_OfACorruptZip_FailsWithAReason_AndDeletesThePackage()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var corrupt = "PK\u0003\u0004"u8.ToArray().Concat(new byte[500]).ToArray();

        var importId = await harness.ImportArchiveAsync(corrupt);

        await using var context = harness.NewContext();
        var import = await context.ArchiveImports.SingleAsync(candidate => candidate.Id == importId);

        import.Status.Should().Be(ArchiveImportStatus.Failed);
        import.Messages.Should().ContainSingle().Which.Should().Contain("not a readable ZIP");
        import.PackagePath.Should().BeNull();
    }

    [Fact]
    public async Task Import_InterruptedByARestart_ResumesAndSkipsWhatItAlreadyImported()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var first = DocxFixture.Create([OldReportParagraph]);
        var zip = Zip(("2012/a.docx", first), ("2013/b.docx", DocxFixture.Create([ThesisParagraph])));

        int importId;

        await using (var context = harness.NewContext())
        await using (var content = new MemoryStream(zip))
        {
            await harness.NewArchiveService(context).QueueImportAsync(new ArchivePackage("a.zip", zip.Length, content, null, null), "admin-id");
            importId = harness.Queue.Enqueued.Single();
        }

        // The first file made it in before the restart.
        await using (var context = harness.NewContext())
        {
            var import = await context.ArchiveImports.SingleAsync();
            import.Status = ArchiveImportStatus.Processing;
            context.ArchiveDocuments.Add(new ArchiveDocument
            {
                Title = "a",
                Year = 2012,
                ArchiveImportId = import.Id,
                ImportedAtUtc = DateTime.UtcNow,
                File = await StoredFileAsync(harness, "a.docx", first),
            });
            await context.SaveChangesAsync();
        }

        await using (var context = harness.NewContext())
        {
            var importer = harness.NewImporter(context);
            (await importer.ResumePendingAsync()).Should().Equal(importId);
            await importer.ImportAsync(importId);
        }

        await using (var context = harness.NewContext())
        {
            var import = await context.ArchiveImports.SingleAsync();

            import.Status.Should().Be(ArchiveImportStatus.Completed);
            import.ImportedFiles.Should().Be(1);
            import.SkippedFiles.Should().Be(1);
            import.Messages.Should().Contain(message => message.StartsWith("Resumed after a restart"));
            (await context.ArchiveDocuments.CountAsync()).Should().Be(2);
        }

        // A finished import is never run again.
        await using (var context = harness.NewContext())
        {
            await harness.NewImporter(context).ImportAsync(importId);
            (await context.ArchiveDocuments.CountAsync()).Should().Be(2);
        }
    }

    // ---------------- queueing ----------------

    [Theory]
    [InlineData("old-work.rar", true, "Upload a .zip file.")]
    [InlineData("old-work.zip", false, "The file is not a ZIP archive.")]
    public async Task QueueImport_RefusesAnythingButAZip(string fileName, bool realZip, string expected)
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var bytes = realZip ? Zip(("a.docx", DocxFixture.Create([OldReportParagraph]))) : Text("Rar!\u001a\u0007 not a zip");

        await using var context = harness.NewContext();
        await using var content = new MemoryStream(bytes);

        var result = await harness.NewArchiveService(context).QueueImportAsync(new ArchivePackage(fileName, bytes.Length, content, null, null), "admin-id");

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Equal(expected);
        harness.Queue.Enqueued.Should().BeEmpty();
        (await context.ArchiveImports.AnyAsync()).Should().BeFalse();
    }

    [Theory]
    [InlineData(2009)]
    [InlineData(2100)]
    public async Task QueueImport_RefusesADefaultYearOutsideTheArchive(int year)
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var zip = Zip(("a.docx", DocxFixture.Create([OldReportParagraph])));

        await using var context = harness.NewContext();
        await using var content = new MemoryStream(zip);

        var result = await harness.NewArchiveService(context).QueueImportAsync(new ArchivePackage("a.zip", zip.Length, content, year, null), "admin-id");

        result.Succeeded.Should().BeFalse();
        result.Errors.Single().Should().StartWith("The default year must be between 2010 and");
    }

    [Fact]
    public async Task QueueImport_IsAudited()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();

        var importId = await harness.ImportArchiveAsync(Zip(("2012/a.docx", DocxFixture.Create([OldReportParagraph]))));

        await using var context = harness.NewContext();
        var entry = await context.AuditLogs.SingleAsync(log => log.Action == Core.Auditing.AuditActions.ArchiveImportQueued);

        entry.EntityId.Should().Be(importId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        entry.UserId.Should().Be("admin-id");
    }

    // ---------------- matching ----------------

    [Fact]
    public async Task Submission_CopyingAnArchivedThesis_IsMatchedAgainstIt_AndOnlyStaffSeeTheAuthor()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        await harness.ImportArchiveAsync(Zip(
            ("manifest.csv", Text("file,title,author,year\nthesis.docx,Winnowing Revisited,Karim Ahmed,2016\n")),
            ("thesis.docx", DocxFixture.Create([ThesisParagraph]))));

        var submissionId = await harness.SubmitAsync(("mine.docx", DocxFixture.Create([NewWork, ThesisParagraph])));
        await harness.AnalyzeAsync(submissionId);

        await using var context = harness.NewContext();
        var review = new ReviewService(context, new AuditService(context, NullLogger<AuditService>.Instance));

        var match = (await review.GetSubmissionAsync(submissionId, harness.TeacherUserId))!.Result!.Matches.Should().ContainSingle().Subject;
        match.ArchiveYear.Should().Be(2016);
        match.MatchedSubmissionId.Should().BeNull();
        match.SourceTitle.Should().Be("Winnowing Revisited");
        match.SourceOwner.Should().Be("Karim Ahmed (2016 archive)");
        match.SimilarityPercent.Should().BeGreaterThan(20);

        var comparison = await review.GetComparisonAsync(match.MatchId, harness.TeacherUserId);
        comparison!.Source.Title.Should().Be("Winnowing Revisited");
        comparison.Source.Owner.Should().Be("Karim Ahmed (2016 archive)");

        var reports = new ReportService(context, new SubmissionAccessService(context), new QuestPdfReportGenerator(), Options.Create(new ReportOptions()));
        var studentText = PdfText((await reports.GetSubmissionReportAsync(submissionId, new SubmissionViewer(harness.StudentUserId, IsStaff: false)))!.Content);
        var staffText = PdfText((await reports.GetSubmissionReportAsync(submissionId, SubmissionViewer.ForTeacher(harness.TeacherUserId)))!.Content);

        studentText.Should().Contain("University archive (2016)").And.NotContain("Karim").And.NotContain("Winnowing Revisited");
        staffText.Should().Contain("Karim Ahmed").And.Contain("Archive 2016");
    }

    // ---------------- browsing and deleting ----------------

    [Fact]
    public async Task Overview_CountsEveryYearFrom2010_ZerosIncluded()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        await harness.ImportArchiveAsync(Zip(
            ("2012/a.docx", DocxFixture.Create([OldReportParagraph])),
            ("2012/b.docx", DocxFixture.Create([ThesisParagraph])),
            ("2015/c.docx", DocxFixture.Create([NewWork]))));

        await using var context = harness.NewContext();
        var overview = await harness.NewArchiveService(context).GetOverviewAsync();

        overview.TotalDocuments.Should().Be(3);
        overview.DocumentsByYear.First().Year.Should().Be(2010);
        overview.DocumentsByYear.Last().Year.Should().Be(DateTime.UtcNow.Year);
        overview.DocumentsByYear.Single(year => year.Year == 2012).Documents.Should().Be(2);
        overview.DocumentsByYear.Single(year => year.Year == 2013).Documents.Should().Be(0);
        overview.RecentImports.Should().ContainSingle().Which.ImportedFiles.Should().Be(3);
    }

    [Fact]
    public async Task Documents_FilterByYearAndSearch()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        await harness.ImportArchiveAsync(Zip(
            ("manifest.csv", Text("file,author\n2012/a.docx,Karim Ahmed\n")),
            ("2012/a.docx", DocxFixture.Create([OldReportParagraph])),
            ("2012/b.docx", DocxFixture.Create([ThesisParagraph])),
            ("2015/c.docx", DocxFixture.Create([NewWork]))));

        await using var context = harness.NewContext();
        var service = harness.NewArchiveService(context);

        (await service.GetDocumentsAsync(new ArchiveQuery(Year: 2012))).Items.Select(document => document.Title).Should().Equal("a", "b");
        (await service.GetDocumentsAsync(new ArchiveQuery(Search: "Karim"))).Items.Select(document => document.Title).Should().Equal("a");

        var all = await service.GetDocumentsAsync(new ArchiveQuery());
        all.Items.Select(document => document.Year).Should().Equal(2015, 2012, 2012);
        all.Items.Should().OnlyContain(document => document.ReadBy == ExtractionMethod.Native && document.WordCount > 0);
    }

    [Fact]
    public async Task Delete_RemovesAnUnmatchedDocument_ButKeepsOneASubmissionMatched()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        await harness.ImportArchiveAsync(Zip(
            ("2012/unmatched.docx", DocxFixture.Create([OldReportParagraph])),
            ("2016/thesis.docx", DocxFixture.Create([ThesisParagraph]))));

        var submissionId = await harness.SubmitAsync(("mine.docx", DocxFixture.Create([NewWork, ThesisParagraph])));
        await harness.AnalyzeAsync(submissionId);

        await using var context = harness.NewContext();
        var service = harness.NewArchiveService(context);
        var documents = await context.ArchiveDocuments.AsNoTracking().Include(document => document.File).ToDictionaryAsync(document => document.Title);

        var kept = await service.DeleteDocumentAsync(documents["thesis"].Id, "admin-id");
        var deleted = await service.DeleteDocumentAsync(documents["unmatched"].Id, "admin-id");

        kept.Succeeded.Should().BeFalse();
        kept.Errors.Single().Should().Contain("has been matched by submissions");
        deleted.Succeeded.Should().BeTrue();

        (await context.ArchiveDocuments.Select(document => document.Title).ToListAsync()).Should().Equal("thesis");
        (await context.UploadedFiles.AnyAsync(file => file.Id == documents["unmatched"].File.Id)).Should().BeFalse();
        (await context.DocumentFingerprints.AnyAsync(print => print.UploadedFileId == documents["unmatched"].File.Id)).Should().BeFalse();

        var openDeleted = () => harness.Storage.OpenReadAsync(documents["unmatched"].File.StoragePath);
        await openDeleted.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task Database_RefusesAFileWithNoOwner()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        await using var context = harness.NewContext();

        context.UploadedFiles.Add(await StoredFileAsync(harness, "orphan.docx", DocxFixture.Create([NewWork])));

        var save = () => context.SaveChangesAsync();
        await save.Should().ThrowAsync<DbUpdateException>();
    }

    private static async Task<UploadedFile> StoredFileAsync(AnalysisTestHarness harness, string fileName, byte[] content)
    {
        await using var stream = new MemoryStream(content);
        var stored = await harness.Storage.StoreAsync(new Core.Files.FileUpload(fileName, content.Length, stream));

        return new UploadedFile
        {
            OriginalFileName = fileName,
            ContentType = stored.ContentType,
            SizeBytes = stored.SizeBytes,
            Sha256Hash = stored.Sha256Hash,
            StoragePath = stored.StoragePath,
        };
    }

    private static string PdfText(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join(" ", document.GetPages().Select(page => string.Join(" ", page.GetWords().Select(word => word.Text))));
    }
}
