using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SmartPlagiarism.Engine.Fingerprinting;
using SmartPlagiarism.Engine.Similarity;
using SmartPlagiarism.Infrastructure.Similarity;
using SmartPlagiarism.Tests.Analysis;
using SmartPlagiarism.Tests.Extraction;

namespace SmartPlagiarism.Tests.Similarity;

/// <summary>
/// The shortlist as the database computes it from the inverted index: most
/// shared fingerprints first, capped, and never the excluded documents.
/// </summary>
public class EfFingerprintIndexTests
{
    private const string Copied =
        "Winnowing selects the minimum hash value within each sliding window of consecutive fingerprints, "
        + "which guarantees that any sufficiently long shared passage between two documents will be found "
        + "while storing only a small fraction of all the hashes that a document produces.";

    private const string HalfCopied =
        "Winnowing selects the minimum hash value within each sliding window of consecutive fingerprints, "
        + "and then the rest of this paragraph goes somewhere else entirely with words of its own choosing.";

    private const string Unrelated =
        "Photosynthesis converts light energy into chemical energy stored in carbohydrate molecules inside "
        + "chloroplasts, driving carbon fixation through a cycle of enzyme catalysed reactions.";

    [Fact]
    public async Task Shortlist_IsOrderedByFingerprintsShared_CappedAndExcluding()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var full = await harness.SubmitAsync(("full.docx", DocxFixture.Create([Copied])));
        var half = await harness.SubmitAsOtherStudentAsync(("half.docx", DocxFixture.Create([HalfCopied])));
        var other = await harness.SubmitAsOtherStudentAsync(("other.docx", DocxFixture.Create([Unrelated])));

        foreach (var id in new[] { full, half, other })
        {
            await harness.AnalyzeAsync(id);
        }

        await using var context = harness.NewContext();
        var fileOf = await context.UploadedFiles.ToDictionaryAsync(file => file.SubmissionId!.Value, file => file.Id);
        var set = new SimilarityPipeline(new EfFingerprintIndex(context), new EfCorpusReader(context),
                CombinedSimilarity.CreateDefault(UniformInverseDocumentFrequency.Instance))
            .Fingerprint(new AnalyzedDocument(0, Copied));

        FingerprintQuery Query(int maxCandidates, params int[] excluded) =>
            new(new HashSet<ulong>(set.DistinctHashes), set.NGramSize, set.WindowSize, 1, maxCandidates, excluded);

        var index = new EfFingerprintIndex(context);

        var all = await index.FindCandidatesAsync(Query(10));
        all.Select(candidate => candidate.DocumentId).Should().Equal(fileOf[full], fileOf[half]);
        all[0].SharedFingerprintCount.Should().Be(set.DistinctHashes.Count);

        (await index.FindCandidatesAsync(Query(1))).Should().ContainSingle()
            .Which.DocumentId.Should().Be(fileOf[full]);

        (await index.FindCandidatesAsync(Query(10, fileOf[full]))).Should().ContainSingle()
            .Which.DocumentId.Should().Be(fileOf[half]);
    }
}
