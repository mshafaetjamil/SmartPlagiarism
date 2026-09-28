using FluentAssertions;
using SmartPlagiarism.Infrastructure.Archive;

namespace SmartPlagiarism.Tests.Archive;

/// <summary>manifest.csv as spreadsheets actually export it.</summary>
public class ArchiveManifestTests
{
    private static ArchiveManifest Parse(string csv) => ArchiveManifest.Parse(new StringReader(csv));

    [Fact]
    public void Parse_FindsColumnsByHeader_InAnyOrder_IgnoringExtras()
    {
        var manifest = Parse("Year,Notes,FILE,Author,Title\r\n2014,scanned copy,2014/a.pdf,Nusrat Jahan,Crop Disease Detection\r\n");

        manifest.Problems.Should().BeEmpty();
        manifest.Find("2014/a.pdf").Should().Be(new ArchiveManifest.Row("Crop Disease Detection", "Nusrat Jahan", 2014, null, null));
    }

    [Fact]
    public void Parse_HandlesQuotedCommasQuotesAndLineBreaks()
    {
        var manifest = Parse("file,title,author\na.docx,\"Rice, Wheat and \"\"Jute\"\"\",\"Ahmed,\nKarim\"\nb.docx,Plain,\n");

        manifest.RowCount.Should().Be(2);
        manifest.Find("a.docx")!.Title.Should().Be("Rice, Wheat and \"Jute\"");
        manifest.Find("a.docx")!.Author.Should().Be("Ahmed,\nKarim");
        manifest.Find("b.docx")!.Author.Should().BeNull("an empty cell is no value");
    }

    [Fact]
    public void Find_MatchesByPathFirst_ThenByUniqueFileName()
    {
        var manifest = Parse("file,title\n.\\2012\\a.docx,By path\n2013/shared.docx,One\n2014/shared.docx,Two\nonly.docx,By name\n");

        manifest.Find("2012/A.DOCX")!.Title.Should().Be("By path", "Windows paths, ./ and case do not matter");
        manifest.Find("somewhere/else/only.docx")!.Title.Should().Be("By name");
        manifest.Find("2014/shared.docx")!.Title.Should().Be("Two");
        manifest.Find("2015/shared.docx").Should().BeNull("two rows share that name, so a name alone is ambiguous");
        manifest.Find("missing.docx").Should().BeNull();
    }

    [Fact]
    public void Parse_ReportsAYearThatIsNotANumber_AndKeepsTheRestOfTheRow()
    {
        var manifest = Parse("file,title,year\na.docx,Thesis,Spring 2014\n");

        manifest.Problems.Should().ContainSingle().Which.Should().Contain("row 2").And.Contain("Spring 2014");
        manifest.Find("a.docx").Should().Be(new ArchiveManifest.Row("Thesis", null, null, null, null));
    }

    [Theory]
    [InlineData("", "manifest.csv is empty.")]
    [InlineData("title,author\nThesis,Karim\n", "manifest.csv has no \"file\" column, so it was ignored.")]
    public void Parse_UnusableManifest_IsReported(string csv, string problem)
    {
        var manifest = Parse(csv);

        manifest.Problems.Should().Equal(problem);
        manifest.RowCount.Should().Be(0);
    }
}
