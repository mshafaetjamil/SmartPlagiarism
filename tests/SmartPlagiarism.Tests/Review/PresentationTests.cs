using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Html;
using SmartPlagiarism.Core.Review;
using SmartPlagiarism.Web.Models;
using SmartPlagiarism.Web.Models.Review;

namespace SmartPlagiarism.Tests.Review;

/// <summary>
/// The two presentation rules Phase 7 is specified by: the colour bands, and
/// that a student's text can never be rendered as HTML.
/// </summary>
public class PresentationTests
{
    [Theory]
    [InlineData("0", SimilarityBand.Low)]
    [InlineData("19.99", SimilarityBand.Low)]
    [InlineData("20", SimilarityBand.Moderate)]
    [InlineData("50", SimilarityBand.Moderate)]
    [InlineData("50.01", SimilarityBand.High)]
    [InlineData("100", SimilarityBand.High)]
    public void SimilarityBands_FollowTheTwentyAndFiftyPercentThresholds(string percent, SimilarityBand expected)
    {
        SimilarityBands.Classify(decimal.Parse(percent, System.Globalization.CultureInfo.InvariantCulture))
            .Should().Be(expected);
    }

    [Theory]
    [InlineData("12.5", "text-bg-success")]
    [InlineData("35", "text-bg-warning")]
    [InlineData("75", "text-bg-danger")]
    public void SimilarityBadge_IsGreenYellowRed(string percent, string cssClass)
    {
        var html = Render(StatusBadge.Similarity(decimal.Parse(percent, System.Globalization.CultureInfo.InvariantCulture)));

        html.Should().Contain(cssClass);
    }

    [Fact]
    public void Highlighting_EncodesEverythingAStudentWrote()
    {
        const string hostile = "<script>alert('x')</script> & \"quotes\" <b>bold</b>";

        var segments = HighlightSegmenter.Segment(hostile, [new HighlightSpan(0, 29, 0)]);
        var html = Render(HighlightRenderer.Render(segments));

        html.Should().NotContain("<script>").And.NotContain("<b>");
        html.Should().Contain("&lt;script&gt;").And.Contain("&amp;").And.Contain("&lt;b&gt;");
        html.Should().Contain("<mark class=\"match-fragment\"").And.Contain("data-fragment=\"0\"");
    }

    private static string Render(IHtmlContent content)
    {
        using var writer = new StringWriter();
        content.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }
}
