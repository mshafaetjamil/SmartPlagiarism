using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.DTOs.Reports;
using SmartPlagiarism.Engine.Matching;
using SmartPlagiarism.Infrastructure.Data;
using SmartPlagiarism.Infrastructure.Services;

namespace SmartPlagiarism.Infrastructure.Reports;

/// <summary>Settings for generated reports.</summary>
public sealed class ReportOptions
{
    public const string SectionName = "Reports";

    /// <summary>Shown in every report's header.</summary>
    public string InstitutionName { get; set; } = "Smart Plagiarism Detection System";
}

/// <inheritdoc cref="IReportService"/>
public class ReportService : IReportService
{
    /// <summary>Passages quoted in a report; the rest are summarised by the matches table.</summary>
    public const int MaxExcerpts = 5;

    /// <summary>Longest quotation, so one long passage cannot run to pages.</summary>
    public const int MaxExcerptLength = 600;

    private readonly AppDbContext _context;
    private readonly ISubmissionAccessService _access;
    private readonly IReportGenerator _generator;
    private readonly ReportOptions _options;

    public ReportService(
        AppDbContext context,
        ISubmissionAccessService access,
        IReportGenerator generator,
        IOptions<ReportOptions> options)
    {
        _context = context;
        _access = access;
        _generator = generator;
        _options = options.Value;
    }

    public async Task<ReportFile?> GetSubmissionReportAsync(
        int submissionId,
        SubmissionViewer viewer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(viewer);

        if (!await _access.CanViewAsync(submissionId, viewer, cancellationToken))
        {
            return null;
        }

        var data = await BuildAsync(submissionId, viewer.IsStaff, cancellationToken);

        return new ReportFile(
            $"originality-report-{submissionId}.pdf",
            "application/pdf",
            _generator.GenerateSubmissionReport(data));
    }

    private async Task<SubmissionReportData> BuildAsync(int submissionId, bool forStaff, CancellationToken cancellationToken)
    {
        var submission = await _context.Submissions
            .AsNoTracking()
            .Where(candidate => candidate.Id == submissionId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.Title,
                candidate.Type,
                candidate.StudentProfileId,
                CourseCode = candidate.Course.Code,
                CourseTitle = candidate.Course.Title,
                DepartmentName = candidate.Course.Department.Name,
                SemesterName = candidate.Semester.Name,
                candidate.SubmittedAtUtc,
                StudentName = candidate.StudentProfile.User.FullName,
                candidate.StudentProfile.StudentNumber,
                candidate.AnalysisStatus,
                candidate.AnalysisError,
                candidate.Status,
                candidate.TeacherComment,
            })
            .SingleAsync(cancellationToken);

        var files = await _context.UploadedFiles
            .AsNoTracking()
            .Where(file => file.SubmissionId == submissionId)
            .OrderBy(file => file.OriginalFileName)
            .Select(file => new { file.OriginalFileName, Extraction = file.ExtractedText })
            .ToListAsync(cancellationToken);

        var result = await _context.PlagiarismResults
            .AsNoTracking()
            .Where(candidate => candidate.SubmissionId == submissionId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.OverallSimilarityPercent,
                candidate.CommonTextPercent,
                candidate.AnalyzedAtUtc,
                candidate.EngineVersion,
            })
            .FirstOrDefaultAsync(cancellationToken);

        List<MatchRow> matches = result is null
            ? []
            : await _context.MatchedDocuments
                .AsNoTracking()
                .Where(match => match.PlagiarismResultId == result.Id && match.MatchedUploadedFileId != null)
                .Select(match => new MatchRow(
                    match.UploadedFileId,
                    match.UploadedFile.OriginalFileName,
                    match.MatchedUploadedFileId!.Value,
                    new MatchSource(
                        match.MatchedUploadedFile!.SubmissionId,
                        match.MatchedUploadedFile.Submission!.Title,
                        (int?)match.MatchedUploadedFile.Submission!.StudentProfileId,
                        match.MatchedUploadedFile.Submission!.StudentProfile.User.FullName,
                        match.MatchedUploadedFile.Submission!.StudentProfile.StudentNumber,
                        match.MatchedUploadedFile.Submission!.Course.Code,
                        (int?)match.MatchedUploadedFile.ArchiveDocument!.Year,
                        match.MatchedUploadedFile.ArchiveDocument!.Title,
                        match.MatchedUploadedFile.ArchiveDocument!.Author),
                    match.SimilarityPercent,
                    match.MatchedFragmentsJson))
                .ToListAsync(cancellationToken);

        matches = [.. matches.OrderByDescending(match => match.SimilarityPercent)];

        string Describe(MatchRow match) => forStaff
            ? match.Source.StaffLabel + (match.Source.IsOwnEarlierWorkOf(submission.StudentProfileId) ? MatchSource.NotCountedNote : string.Empty)
            : match.Source.StudentLabel(submission.StudentProfileId);

        return new SubmissionReportData(
            _options.InstitutionName,
            submission.DepartmentName,
            forStaff,
            submission.Id,
            submission.Title,
            submission.Type,
            submission.CourseCode,
            submission.CourseTitle,
            submission.SemesterName,
            submission.SubmittedAtUtc,
            submission.StudentName,
            submission.StudentNumber,
            submission.AnalysisStatus,
            submission.AnalysisError,
            result?.OverallSimilarityPercent,
            result?.CommonTextPercent ?? 0m,
            result?.AnalyzedAtUtc,
            result?.EngineVersion,
            files.Select(file => new ReportFileLine(
                    file.OriginalFileName,
                    file.Extraction?.ExtractionMethod,
                    file.Extraction?.WordCount,
                    file.Extraction?.Warnings ?? []))
                .ToList(),
            matches.Select(match => new ReportMatchLine(
                    Describe(match),
                    match.OwnFileName,
                    match.SimilarityPercent,
                    MatchedFragmentSerializer.Deserialize(match.FragmentsJson).Count))
                .ToList(),
            await BuildExcerptsAsync(matches, forStaff, Describe, result?.AnalyzedAtUtc, cancellationToken),
            submission.Status,
            submission.TeacherComment,
            DateTime.UtcNow);
    }

    /// <summary>
    /// The longest matched passages across all matches - the ones a reader most
    /// needs to see - quoted from both documents.
    /// </summary>
    private async Task<IReadOnlyList<ReportExcerpt>> BuildExcerptsAsync(
        IReadOnlyList<MatchRow> matches,
        bool forStaff,
        Func<MatchRow, string> describe,
        DateTime? analyzedAtUtc,
        CancellationToken cancellationToken)
    {
        var top = matches
            .SelectMany(match => MatchedFragmentSerializer.Deserialize(match.FragmentsJson)
                .Select(fragment => (Match: match, Fragment: fragment)))
            .OrderByDescending(entry => entry.Fragment.TargetLength)
            .Take(MaxExcerpts)
            .ToList();

        if (top.Count == 0)
        {
            return [];
        }

        // Only the texts the quotations come from - never another student's text
        // for a student's copy.
        var fileIds = top.Select(entry => entry.Match.OwnFileId)
            .Concat(forStaff ? top.Select(entry => entry.Match.SourceFileId) : [])
            .Distinct()
            .ToList();

        var texts = await _context.ExtractedTexts
            .AsNoTracking()
            .Where(text => fileIds.Contains(text.UploadedFileId))
            .ToDictionaryAsync(text => text.UploadedFileId, text => (text.FullText, text.ExtractedAtUtc), cancellationToken);

        return top
            .Select(entry => new ReportExcerpt(
                describe(entry.Match),
                entry.Match.SimilarityPercent,
                QuoteIfCurrent(entry.Match.OwnFileId, entry.Fragment.TargetStart, entry.Fragment.TargetEnd),
                forStaff
                    ? QuoteIfCurrent(entry.Match.SourceFileId, entry.Fragment.SourceStart, entry.Fragment.SourceEnd)
                    : null))
            .ToList();

        // The stored offsets index the text as it was when the analysis ran. A
        // text read again since may have shifted, and quoting it would put the
        // wrong words in front of a disciplinary panel.
        string QuoteIfCurrent(int fileId, int start, int end) =>
            !texts.TryGetValue(fileId, out var text)
                ? string.Empty
                : analyzedAtUtc is { } analysed && text.ExtractedAtUtc > analysed
                    ? StaleQuoteNote
                    : Quote(text.FullText, start, end);
    }

    internal const string StaleQuoteNote =
        "[This document was read again after the analysis ran, so this passage cannot be quoted reliably. "
        + "Re-run the analysis to refresh it.]";

    /// <summary>
    /// The passage's text with runs of whitespace - PDF line padding, say -
    /// collapsed, cut at a word boundary if too long. Offsets are clipped, since
    /// the text may have been re-extracted since the analysis.
    /// </summary>
    internal static string Quote(string? text, int start, int end)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        start = Math.Clamp(start, 0, text.Length);
        end = Math.Clamp(end, start, text.Length);

        var collapsed = new StringBuilder(end - start);
        var pendingSpace = false;

        foreach (var character in text.AsSpan(start, end - start))
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = collapsed.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                collapsed.Append(' ');
                pendingSpace = false;
            }

            collapsed.Append(character);
        }

        if (collapsed.Length <= MaxExcerptLength)
        {
            return collapsed.ToString();
        }

        var cut = collapsed.ToString(0, MaxExcerptLength);
        var lastSpace = cut.LastIndexOf(' ');

        return (lastSpace > MaxExcerptLength / 2 ? cut[..lastSpace] : cut) + " ...";
    }

    private sealed record MatchRow(
        int OwnFileId,
        string OwnFileName,
        int SourceFileId,
        MatchSource Source,
        decimal SimilarityPercent,
        string FragmentsJson);
}
