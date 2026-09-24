using Microsoft.EntityFrameworkCore;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.DTOs.Admin;
using SmartPlagiarism.Core.Enums;
using SmartPlagiarism.Core.Review;
using SmartPlagiarism.Infrastructure.Data;

namespace SmartPlagiarism.Infrastructure.Admin;

/// <inheritdoc cref="IStatisticsService"/>
/// <remarks>
/// Averages are taken over the score as a double: exact enough for a statistic,
/// and aggregatable by every provider, SQLite included.
/// </remarks>
public class StatisticsService : IStatisticsService
{
    /// <summary>Most recent failures listed; older ones are still in the audit trail.</summary>
    public const int MaxFailedListed = 50;

    private readonly AppDbContext _context;

    public StatisticsService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<SystemStatisticsDto> GetStatisticsAsync(int months = 12, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(months, 1);

        var submissions = _context.Submissions.AsNoTracking();
        var results = _context.PlagiarismResults.AsNoTracking();

        return new SystemStatisticsDto(
            await submissions.CountAsync(cancellationToken),
            await results.CountAsync(cancellationToken),
            await results.AverageAsync(result => (double?)result.OverallSimilarityPercent, cancellationToken),
            await SubmissionsByMonthAsync(months, cancellationToken),
            await ExtractionMethodsAsync(cancellationToken),
            await SimilarityBandsAsync(cancellationToken),
            await submissions
                .Where(submission => submission.AnalysisStatus == AnalysisStatus.Failed)
                .OrderByDescending(submission => submission.SubmittedAtUtc)
                .Take(MaxFailedListed)
                .Select(submission => new FailedAnalysisDto(
                    submission.Id,
                    submission.Title,
                    submission.StudentProfile.User.FullName,
                    submission.SubmittedAtUtc,
                    submission.AnalysisError))
                .ToListAsync(cancellationToken));
    }

    /// <summary>
    /// One entry per month, oldest first, including months with no submissions -
    /// a gap in the chart is information too.
    /// </summary>
    private async Task<IReadOnlyList<MonthlySubmissionsDto>> SubmissionsByMonthAsync(int months, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var firstMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-(months - 1));

        var counts = await _context.Submissions
            .AsNoTracking()
            .Where(submission => submission.SubmittedAtUtc >= firstMonth)
            .GroupBy(submission => new { submission.SubmittedAtUtc.Year, submission.SubmittedAtUtc.Month })
            .Select(group => new
            {
                group.Key.Year,
                group.Key.Month,
                Count = group.Count(),
                Average = group.Average(submission => (double?)submission.PlagiarismResult!.OverallSimilarityPercent),
            })
            .ToListAsync(cancellationToken);

        return Enumerable.Range(0, months)
            .Select(offset => firstMonth.AddMonths(offset))
            .Select(month =>
            {
                var found = counts.FirstOrDefault(count => count.Year == month.Year && count.Month == month.Month);
                return new MonthlySubmissionsDto(month.Year, month.Month, found?.Count ?? 0, found?.Average);
            })
            .ToList();
    }

    private async Task<IReadOnlyList<ExtractionMethodCountDto>> ExtractionMethodsAsync(CancellationToken cancellationToken)
    {
        var counts = await _context.ExtractedTexts
            .AsNoTracking()
            .GroupBy(text => text.ExtractionMethod)
            .Select(group => new { Method = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return Enum.GetValues<ExtractionMethod>()
            .Select(method => new ExtractionMethodCountDto(method, counts.FirstOrDefault(count => count.Method == method)?.Count ?? 0))
            .ToList();
    }

    private async Task<IReadOnlyList<SimilarityBandCountDto>> SimilarityBandsAsync(CancellationToken cancellationToken)
    {
        var results = _context.PlagiarismResults.AsNoTracking();

        return
        [
            new(SimilarityBand.Low, await results.CountAsync(
                result => result.OverallSimilarityPercent < SimilarityBands.ModerateFrom, cancellationToken)),
            new(SimilarityBand.Moderate, await results.CountAsync(
                result => result.OverallSimilarityPercent >= SimilarityBands.ModerateFrom
                    && result.OverallSimilarityPercent <= SimilarityBands.HighAbove, cancellationToken)),
            new(SimilarityBand.High, await results.CountAsync(
                result => result.OverallSimilarityPercent > SimilarityBands.HighAbove, cancellationToken)),
        ];
    }
}
