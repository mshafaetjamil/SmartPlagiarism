using FluentAssertions;
using SmartPlagiarism.Core.Review;

namespace SmartPlagiarism.Tests.Review;

/// <summary>
/// Cutting text into plain and highlighted segments from stored offsets. The
/// property every case must keep: the segments concatenate back to the text,
/// exactly, whatever the offsets say.
/// </summary>
public class HighlightSegmenterTests
{
    private const string Text = "The quick brown fox jumps over the lazy dog.";

    private static string Joined(IEnumerable<HighlightSegment> segments) =>
        string.Concat(segments.Select(segment => segment.Text));

    [Fact]
    public void NoPassages_GivesOnePlainSegment()
    {
        var segments = HighlightSegmenter.Segment(Text, []);

        segments.Should().Equal(new HighlightSegment(Text, null));
    }

    [Fact]
    public void OnePassage_IsCutOutOfTheMiddle()
    {
        // "quick brown fox"
        var segments = HighlightSegmenter.Segment(Text, [new HighlightSpan(4, 19, 0)]);

        segments.Should().Equal(
            new HighlightSegment("The ", null),
            new HighlightSegment("quick brown fox", 0),
            new HighlightSegment(" jumps over the lazy dog.", null));
    }

    [Fact]
    public void OverlappingPassages_TheLowerNumberedOneOwnsTheOverlap()
    {
        // Passage 0 is "quick brown", passage 1 is "brown fox".
        var segments = HighlightSegmenter.Segment(Text, [new HighlightSpan(10, 19, 1), new HighlightSpan(4, 15, 0)]);

        segments.Where(segment => segment.IsHighlighted).Should().Equal(
            new HighlightSegment("quick brown", 0),
            new HighlightSegment(" fox", 1));

        Joined(segments).Should().Be(Text);
    }

    [Fact]
    public void OffsetsBeyondTheText_AreClipped_AndInvalidOnesDropped()
    {
        // Offsets from before a re-extraction can point past the current text.
        var segments = HighlightSegmenter.Segment(Text,
        [
            new HighlightSpan(40, 500, 0),
            new HighlightSpan(-5, 3, 1),
            new HighlightSpan(20, 20, 2),
            new HighlightSpan(30, 25, 3),
        ]);

        segments.Should().Contain(new HighlightSegment("dog.", 0));
        segments.Should().Contain(new HighlightSegment("The", 1));
        segments.Should().NotContain(segment => segment.FragmentIndex == 2 || segment.FragmentIndex == 3);
        Joined(segments).Should().Be(Text);
    }

    [Fact]
    public void AdjacentPassages_StaySeparate()
    {
        var segments = HighlightSegmenter.Segment("abcdef", [new HighlightSpan(0, 3, 0), new HighlightSpan(3, 6, 1)]);

        segments.Should().Equal(new HighlightSegment("abc", 0), new HighlightSegment("def", 1));
    }

    [Fact]
    public void EmptyText_GivesNoSegments()
    {
        HighlightSegmenter.Segment(string.Empty, [new HighlightSpan(0, 5, 0)]).Should().BeEmpty();
        HighlightSegmenter.Segment(null, []).Should().BeEmpty();
    }
}
