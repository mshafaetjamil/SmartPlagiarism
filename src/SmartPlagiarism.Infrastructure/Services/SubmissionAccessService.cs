using Microsoft.EntityFrameworkCore;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Infrastructure.Data;

namespace SmartPlagiarism.Infrastructure.Services;

/// <inheritdoc cref="ISubmissionAccessService"/>
public class SubmissionAccessService : ISubmissionAccessService
{
    private readonly AppDbContext _context;

    public SubmissionAccessService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<bool> CanViewAsync(
        int submissionId,
        SubmissionViewer viewer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(viewer);

        var submissions = _context.Submissions.AsNoTracking().Where(submission => submission.Id == submissionId);

        if (viewer.ApiKeyId is { } apiKeyId)
        {
            submissions = submissions.Where(submission => submission.ApiKeyId == apiKeyId);
        }
        else if (!viewer.IsStaff)
        {
            // Ownership is part of the query, as everywhere a student reads a
            // submission: another student's id is simply not found.
            submissions = submissions.Where(submission => submission.StudentProfile.UserId == viewer.UserId);
        }

        return await submissions.AnyAsync(cancellationToken);
    }
}
