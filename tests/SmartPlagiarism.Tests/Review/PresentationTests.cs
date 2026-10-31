using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Html;
using SmartPlagiarism.Core.Enums;
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

    [Theory]
    [InlineData("19.99", "bg-success", "text-success")]
    [InlineData("20", "bg-warning", "text-warning-emphasis")]
    [InlineData("41", "bg-warning", "text-warning-emphasis")]
    [InlineData("50", "bg-warning", "text-warning-emphasis")]
    [InlineData("50.01", "bg-danger", "text-danger")]
    public void SimilarityBar_IsColouredByTheSameBands(string percent, string fillClass, string numberClass)
    {
        var value = decimal.Parse(percent, System.Globalization.CultureInfo.InvariantCulture);

        var html = Render(StatusBadge.SimilarityBar(value));

        html.Should().Contain($"stat-bar-fill {fillClass}").And.Contain($"similarity-bar-value {numberClass}");
        html.Should().Contain($"data-bar-width=\"{value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}\"");
    }

    [Fact]
    public void SimilarityBar_KeepsItsWidthReadableInAnyCulture_AndHasNoInlineStyle()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

        try
        {
            var html = Render(StatusBadge.SimilarityBar(63.16m));

            html.Should().Contain("data-bar-width=\"63.16\"").And.Contain(">63.16%<");
            html.Should().NotContain("style=", "the Content-Security-Policy refuses style attributes");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("-5", "0")]
    [InlineData("140", "100")]
    public void SimilarityBar_NeverDrawsOutsideItsTrack(string percent, string width)
    {
        var html = Render(StatusBadge.SimilarityBar(decimal.Parse(percent, System.Globalization.CultureInfo.InvariantCulture)));

        html.Should().Contain($"data-bar-width=\"{width}\"");
    }

    [Fact]
    public void SimilarityBar_IsADash_WhenThereIsNoScore()
    {
        var html = Render(StatusBadge.SimilarityBar(null));

        html.Should().Contain(">-<").And.NotContain("stat-bar");
    }

    [Theory]
    [InlineData(AnalysisStatus.Queued, "Queued")]
    [InlineData(AnalysisStatus.Processing, "Processing")]
    [InlineData(AnalysisStatus.Failed, "Failed")]
    public void StatusPills_ShowAnUnfinishedAnalysis_BesideTheDecision(AnalysisStatus analysis, string pill)
    {
        var html = Render(StatusBadge.Status(analysis, SubmissionStatus.PendingReview));

        html.Should().Contain($">{pill}<").And.Contain(">Pending review<");
    }

    [Fact]
    public void StatusPills_DropACompletedAnalysis()
    {
        var html = Render(StatusBadge.Status(AnalysisStatus.Completed, SubmissionStatus.Rejected));

        html.Should().Contain(">Rejected<").And.NotContain("Completed");
    }

    [Theory]
    [InlineData("19.99", "Low similarity")]
    [InlineData("20", "Moderate similarity")]
    [InlineData("50", "Moderate similarity")]
    [InlineData("50.01", "High similarity")]
    public void BandLabel_FollowsTheSameThresholds(string percent, string label)
    {
        StatusBadge.SimilarityBandLabel(decimal.Parse(percent, System.Globalization.CultureInfo.InvariantCulture)).Should().Be(label);
    }

    [Fact]
    public void StatTile_ColoursAnAverageByItsBand_AndShowsADashWithoutOne()
    {
        var moderate = StatTileViewModel.Similarity("Average", 41m);
        var none = StatTileViewModel.Similarity("Average", null);

        moderate.Value.Should().Be("41.00%");
        moderate.ValueClass.Should().Be("text-warning-emphasis", "41% is below the 50% line, so it is amber and not red");
        none.Value.Should().Be("-");
        none.ValueClass.Should().BeNull();
    }

    [Fact]
    public void BarWidth_IsAPercentageOfTheLargest_InAnyCulture_AndZeroWhenThereIsNothing()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

        try
        {
            StatusBadge.BarWidth(1, 3).Should().Be("33.3");
            StatusBadge.BarWidth(0, 0).Should().Be("0");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
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
