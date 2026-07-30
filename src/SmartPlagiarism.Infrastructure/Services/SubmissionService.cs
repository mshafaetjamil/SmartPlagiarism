using Microsoft.EntityFrameworkCore;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.DTOs.Submissions;
using SmartPlagiarism.Core.Entities;
using SmartPlagiarism.Core.Files;
using SmartPlagiarism.Infrastructure.Data;

namespace SmartPlagiarism.Infrastructure.Services;

/// <inheritdoc cref="ISubmissionService"/>
public class SubmissionService : ISubmissionService
{
    private readonly AppDbContext _context;
    private readonly IFileStorageService _fileStorage;
    private readonly IFileUploadValidator _validator;

    public SubmissionService(
        AppDbContext context,
        IFileStorageService fileStorage,
        IFileUploadValidator validator)
    {
        _context = context;
        _fileStorage = fileStorage;
        _validator = validator;
    }

    public async Task<CreateSubmissionResult> CreateAsync(
        CreateSubmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            errors.Add("A title is required.");
        }

        if (request.Files.Count == 0)
        {
            errors.Add("Attach at least one file.");
        }
        else if (request.Files.Count > UploadConstraints.MaxFilesPerSubmission)
        {
            errors.Add($"A submission may contain at most {UploadConstraints.MaxFilesPerSubmission} files.");
        }

        var studentProfileId = await _context.StudentProfiles
            .AsNoTracking()
            .Where(profile => profile.UserId == request.StudentUserId)
            .Select(profile => (int?)profile.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (studentProfileId is null)
        {
            errors.Add("Your account is not linked to a student profile yet. Contact an administrator.");
        }

        if (!await _context.Courses.AnyAsync(course => course.Id == request.CourseId, cancellationToken))
        {
            errors.Add("Select a valid course.");
        }

        if (!await _context.Semesters.AnyAsync(semester => semester.Id == request.SemesterId, cancellationToken))
        {
            errors.Add("Select a valid semester.");
        }

        // Validate every file up front. Storing some files and then discovering the
        // last one is invalid would leave orphaned blobs and a half-made submission.
        var validations = new List<FileValidationResult>(request.Files.Count);

        foreach (var file in request.Files)
        {
            var validation = _validator.Validate(file.FileName, file.SizeBytes, file.Content);
            validations.Add(validation);

            if (!validation.IsValid)
            {
                errors.Add(validation.ErrorMessage!);
            }
        }

        if (errors.Count > 0)
        {
            return CreateSubmissionResult.Failure(errors);
        }

        var submission = new Submission
        {
            StudentProfileId = studentProfileId!.Value,
            CourseId = request.CourseId,
            SemesterId = request.SemesterId,
            Title = request.Title.Trim(),
            Type = request.Type,
            SubmittedAtUtc = DateTime.UtcNow,

            // AnalysisStatus starts at Queued via the entity's initializer; Phase 6's
            // worker is what moves it on from there.
        };

        for (var index = 0; index < request.Files.Count; index++)
        {
            var stored = await _fileStorage.StoreAsync(request.Files[index], cancellationToken);

            submission.Files.Add(new UploadedFile
            {
                // The validated, directory-stripped name - kept for display only.
                OriginalFileName = validations[index].SafeFileName!,

                // Canonical type for the detected format, not the browser's claim.
                ContentType = stored.ContentType,
                SizeBytes = stored.SizeBytes,
                Sha256Hash = stored.Sha256Hash,
                StoragePath = stored.StoragePath,
            });
        }

        _context.Submissions.Add(submission);
        await _context.SaveChangesAsync(cancellationToken);

        return CreateSubmissionResult.Success(submission.Id);
    }

    public async Task<IReadOnlyList<SubmissionListItemDto>> GetForStudentAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Submissions
            .AsNoTracking()
            .Where(submission => submission.StudentProfile.UserId == userId)
            .OrderByDescending(submission => submission.SubmittedAtUtc)
            .Select(submission => new SubmissionListItemDto(
                submission.Id,
                submission.Title,
                submission.Course.Code,
                submission.Semester.Name,
                submission.Type,
                submission.SubmittedAtUtc,
                submission.AnalysisStatus,
                submission.Status,
                submission.Files.Count,
                submission.PlagiarismResult != null
                    ? submission.PlagiarismResult.OverallSimilarityPercent
                    : null))
            .ToListAsync(cancellationToken);
    }

    public async Task<SubmissionDetailDto?> GetDetailForStudentAsync(
        int submissionId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        // Ownership is part of the WHERE clause rather than a check after loading,
        // so another student's submission is simply not found.
        return await _context.Submissions
            .AsNoTracking()
            .Where(submission => submission.Id == submissionId
                && submission.StudentProfile.UserId == userId)
            .Select(submission => new SubmissionDetailDto(
                submission.Id,
                submission.Title,
                submission.Course.Code,
                submission.Course.Title,
                submission.Semester.Name,
                submission.Type,
                submission.SubmittedAtUtc,
                submission.AnalysisStatus,
                submission.Status,
                submission.TeacherComment,
                submission.Files
                    .OrderBy(file => file.OriginalFileName)
                    .Select(file => new SubmissionFileDto(
                        file.Id,
                        file.OriginalFileName,
                        file.ContentType,
                        file.SizeBytes,
                        file.Sha256Hash))
                    .ToList(),
                submission.PlagiarismResult != null
                    ? new SubmissionResultDto(
                        submission.PlagiarismResult.OverallSimilarityPercent,
                        submission.PlagiarismResult.AnalyzedAtUtc,
                        submission.PlagiarismResult.EngineVersion,
                        submission.PlagiarismResult.Matches.Count)
                    : null))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<SubmissionFormOptionsDto> GetFormOptionsAsync(CancellationToken cancellationToken = default)
    {
        var courses = await _context.Courses
            .AsNoTracking()
            .OrderBy(course => course.Code)
            .Select(course => new CourseOptionDto(course.Id, course.Code, course.Title))
            .ToListAsync(cancellationToken);

        var semesters = await _context.Semesters
            .AsNoTracking()
            .OrderByDescending(semester => semester.StartDateUtc)
            .Select(semester => new SemesterOptionDto(semester.Id, semester.Name))
            .ToListAsync(cancellationToken);

        return new SubmissionFormOptionsDto(courses, semesters);
    }
}
