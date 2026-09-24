using FluentAssertions;
using SmartPlagiarism.Engine.Matching;
using SmartPlagiarism.Engine.Similarity;

namespace SmartPlagiarism.Tests.Similarity;

/// <summary>
/// Fragment offsets drive the side-by-side highlighting, so the tests here do not
/// trust the numbers — they substring the actual documents with the reported
/// offsets and check what comes back.
/// </summary>
public class FragmentLocatorTests
{
    private const string CopiedParagraph =
        "the fingerprint index shortlists candidate documents so that the expensive pairwise "
        + "comparison only ever runs against a handful of documents rather than the entire corpus";

    [Fact]
    public void CopiedParagraph_IsLocatedInBothDocuments()
    {
        var source = new AnalyzedDocument(1,
            $"An introductory sentence of the original work. {CopiedParagraph} A closing sentence.");

        var target = new AnalyzedDocument(2,
            $"A different opening written by another student. {CopiedParagraph} Followed by unrelated material.");

        var fragments = FragmentLocator.Locate(source, target);

        fragments.Should().NotBeEmpty();

        var fragment = fragments[0];

        // The real assertion: read the text back out at the reported offsets.
        var fromSource = source.Text[fragment.SourceStart..fragment.SourceEnd];
        var fromTarget = target.Text[fragment.TargetStart..fragment.TargetEnd];

        fromSource.Should().Be(CopiedParagraph);
        fromTarget.Should().Be(CopiedParagraph);
        fragment.Similarity.Should().Be(1.0, "the passage was copied verbatim with no gaps");
    }

    [Fact]
    public void OffsetsAreCorrectEvenWhenThePassageSitsAtDifferentPlaces()
    {
        var source = new AnalyzedDocument(1, $"{CopiedParagraph} and then the original continues at length here");
        var target = new AnalyzedDocument(2,
            $"A much longer preamble that pushes everything along before reaching it. {CopiedParagraph}");

        var fragment = FragmentLocator.Locate(source, target)[0];

        fragment.SourceStart.Should().Be(0, "the passage opens the source document");
        fragment.TargetStart.Should().BeGreaterThan(50, "it appears much later in the target");

        source.Text[fragment.SourceStart..fragment.SourceEnd].Should().Be(CopiedParagraph);
        target.Text[fragment.TargetStart..fragment.TargetEnd].Should().Be(CopiedParagraph);
    }

    [Fact]
    public void UnrelatedDocuments_ProduceNoFragments()
    {
        var source = new AnalyzedDocument(1, "Photosynthesis converts light energy into chemical energy in plants.");
        var target = new AnalyzedDocument(2, "Quarterly revenue exceeded every forecast despite currency headwinds.");

        FragmentLocator.Locate(source, target).Should().BeEmpty();
    }

    [Fact]
    public void ShortCommonPhrases_AreIgnoredAsNoise()
    {
        // "on the other hand" is four words: real English, not evidence of copying.
        var source = new AnalyzedDocument(1, "The results were poor. On the other hand the method was sound.");
        var target = new AnalyzedDocument(2, "Costs rose sharply. On the other hand revenue also grew.");

        FragmentLocator.Locate(source, target)
            .Should().BeEmpty("a fragment must span at least eight words to be reported");
    }

    [Fact]
    public void ASingleChangedWord_DoesNotSplitOnePassageIntoTwo()
    {
        var source = new AnalyzedDocument(1,
            "the fingerprint index shortlists candidate documents before the expensive pairwise "
            + "comparison runs against a handful of documents rather than the entire corpus");

        // Same passage with one word swapped in the middle.
        var target = new AnalyzedDocument(2,
            "the fingerprint index shortlists candidate documents before the costly pairwise "
            + "comparison runs against a handful of documents rather than the entire corpus");

        var fragments = FragmentLocator.Locate(source, target);

        fragments.Should().ContainSingle("gap bridging keeps an edited passage as one fragment");
        fragments[0].Similarity.Should().BeLessThan(1.0, "the bridged gap must be reflected in the score");
        fragments[0].Similarity.Should().BeGreaterThan(0.5);
    }

    [Fact]
    public void MultipleSeparatePassages_AreReportedSeparately()
    {
        const string first = "the fingerprint index shortlists candidate documents before expensive comparison begins";
        const string second = "winnowing selects the minimum hash within each sliding window of the document";

        var source = new AnalyzedDocument(1, $"{first} ... completely unrelated filler ... {second}");
        var target = new AnalyzedDocument(2,
            $"{first} ... a totally different bridge of text here ... {second}");

        FragmentLocator.Locate(source, target).Should().HaveCount(2);
    }

    [Fact]
    public void FragmentsAreOrderedLongestFirst()
    {
        const string longPassage = "the fingerprint index shortlists candidate documents before the expensive "
            + "pairwise comparison runs against only a handful of documents";
        const string shortPassage = "winnowing selects the minimum hash in each window";

        var source = new AnalyzedDocument(1, $"{shortPassage} ... filler ... {longPassage}");
        var target = new AnalyzedDocument(2, $"{shortPassage} ... other filler ... {longPassage}");

        var fragments = FragmentLocator.Locate(source, target);

        fragments.Should().HaveCountGreaterThan(1);
        fragments[0].TargetLength.Should().BeGreaterThan(fragments[^1].TargetLength);
    }

    [Fact]
    public void FragmentsSurviveJsonRoundTrip()
    {
        var source = new AnalyzedDocument(1, $"Opening. {CopiedParagraph} Closing.");
        var target = new AnalyzedDocument(2, $"Different opening. {CopiedParagraph} Different closing.");

        var original = FragmentLocator.Locate(source, target);

        var json = MatchedFragmentSerializer.Serialize(original);
        var restored = MatchedFragmentSerializer.Deserialize(json);

        restored.Should().BeEquivalentTo(original);
        json.Should().Contain("sourceStart").And.Contain("targetEnd");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DeserializingNothing_GivesAnEmptyList(string? json)
    {
        MatchedFragmentSerializer.Deserialize(json).Should().BeEmpty();
    }
}
