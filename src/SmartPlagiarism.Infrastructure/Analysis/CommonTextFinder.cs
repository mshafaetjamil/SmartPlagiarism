using Microsoft.EntityFrameworkCore;
using SmartPlagiarism.Engine.Similarity;
using SmartPlagiarism.Infrastructure.Data;

namespace SmartPlagiarism.Infrastructure.Analysis;

/// <summary>
/// Decides which of a submission's fingerprints are common text in its course:
/// shared by at least half of the other students who submitted to it, and by at
/// least <see cref="MinimumStudents"/> of them.
///
/// A cover page, the declaration everyone signs, the assignment brief pasted in
/// - these match between every pair of reports in a class, and would otherwise
/// list most of a student's classmates as sources at a percent or two each,
/// burying the matches that matter. Copying is different: even a passage passed
/// around a group is rarely shared by half a class. So common text is set
/// aside, and the teacher is told how much was, rather than it being hidden.
/// </summary>
public class CommonTextFinder
{
    /// <summary>Below this many other students, nothing is common enough to tell.</summary>
    public const int MinimumStudents = 5;

    /// <summary>Share of the other students a fingerprint must appear in.</summary>
    public const double MinimumShare = 0.5;

    private readonly AppDbContext _context;
    private readonly SimilarityPipeline _pipeline;

    public CommonTextFinder(AppDbContext context, SimilarityPipeline pipeline)
    {
        _context = context;
        _pipeline = pipeline;
    }

    public async Task<IReadOnlySet<ulong>> FindAsync(
        int courseId,
        int studentProfileId,
        IEnumerable<AnalyzedDocument> documents,
        CancellationToken cancellationToken = default)
    {
        var otherStudents = await _context.Submissions
            .Where(submission => submission.CourseId == courseId && submission.StudentProfileId != studentProfileId)
            .Select(submission => submission.StudentProfileId)
            .Distinct()
            .CountAsync(cancellationToken);

        if (otherStudents < MinimumStudents)
        {
            return new HashSet<ulong>();
        }

        var threshold = Math.Max(MinimumStudents, (int)Math.Ceiling(otherStudents * MinimumShare));

        var hashes = documents
            .SelectMany(document => _pipeline.Fingerprint(document).DistinctHashes)
            .Distinct()
            .Select(hash => unchecked((long)hash))
            .ToList();

        var common = await _context.FingerprintHashes
            .Where(row => hashes.Contains(row.Hash)
                && row.DocumentFingerprint.UploadedFile.Submission!.CourseId == courseId
                && row.DocumentFingerprint.UploadedFile.Submission!.StudentProfileId != studentProfileId)
            .Select(row => new { row.Hash, row.DocumentFingerprint.UploadedFile.Submission!.StudentProfileId })
            .Distinct()
            .GroupBy(row => row.Hash)
            .Where(group => group.Count() >= threshold)
            .Select(group => group.Key)
            .ToListAsync(cancellationToken);

        return common.Select(hash => unchecked((ulong)hash)).ToHashSet();
    }
}
