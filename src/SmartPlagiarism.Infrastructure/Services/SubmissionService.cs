using Microsoft.EntityFrameworkCore;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.DTOs;
using SmartPlagiarism.Core.DTOs.Analysis;
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
    private readonly IAnalysisQueue _analysisQueue;
    private readonly ISubmissionAccessService _access;

    public SubmissionService(
        AppDbContext context,
        IFileStorageService fileStorage,
        IFileUploadValidator validator,
        IAnalysisQueue analysisQueue,
        ISubmissionAccessService access)
    {
        _context = context;
        _fileStorage = fileStorage;
        _validator = validator;
        _analysisQueue = analysisQueue;
        _access = access;
    }

    public async Task<CreateSubmissionResult> CreateAsync(
        CreateSubmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<string>();
        var title = request.Title?.Trim() ?? string.Empty;

        // Checked here as well as on the web form and the API model, since both
        // reach this method and a title past the column's length would fail the save.
        if (title.Length == 0)
        {
            errors.Add("A title is required.");
        }
        else if (title.Length is < Submission.MinTitleLength or > Submission.MaxTitleLength)
        {
            errors.Add($"The title must be {Submission.MinTitleLength} to {Submission.MaxTitleLength} characters long.");
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
            Title = title,
            Type = request.Type,
            SubmittedAtUtc = DateTime.UtcNow,
            ApiKeyId = request.ApiKeyId,

            // AnalysisStatus starts at Queued via the entity's initializer; the
            // background worker is what moves it on from there.
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

        // Queued only once saved, so the worker always finds the row it is given.
        await _analysisQueue.EnqueueAsync(submission.Id, cancellationToken);

        return CreateSubmissionResult.Success(submission.Id);
    }

    public Task<PagedResult<SubmissionListItemDto>> GetForStudentAsync(
        string userId,
        int page,
        CancellationToken cancellationToken = default)
    {
        return _context.Submissions
            .AsNoTracking()
            .Where(submission => submission.StudentProfile.UserId == userId)
            .OrderByDescending(submission => submission.SubmittedAtUtc)
            .ThenByDescending(submission => submission.Id)
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
            .ToPagedResultAsync(page, Paging.DefaultPageSize, cancellationToken);
    }

    public Task<SubmissionDetailDto?> GetDetailForStudentAsync(
        int submissionId,
        string userId,
        CancellationToken cancellationToken = default) =>
        GetDetailAsync(submissionId, SubmissionViewer.ForStudent(userId), cancellationToken);

    public async Task<SubmissionDetailDto?> GetDetailAsync(
        int submissionId,
        SubmissionViewer viewer,
        CancellationToken cancellationToken = default)
    {
        // The shared access rule, so the web page and the REST API cannot drift
        // apart on who may see what.
        if (!await _access.CanViewAsync(submissionId, viewer, cancellationToken))
        {
            return null;
        }

        return await _context.Submissions
            .AsNoTracking()
            .Where(submission => submission.Id == submissionId)
            .Select(submission => new SubmissionDetailDto(
                submission.Id,
                submission.Title,
                submission.Course.Code,
                submission.Course.Title,
                submission.Semester.Name,
                submission.Type,
                submission.SubmittedAtUtc,
                submission.AnalysisStatus,
                submission.AnalysisError,
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
                        submission.PlagiarismResult.CommonTextPercent,
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

    public async Task<string?> FindStudentUserIdAsync(string studentNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(studentNumber))
        {
            return null;
        }

        var number = studentNumber.Trim();

        return await _context.StudentProfiles
            .AsNoTracking()
            .Where(profile => profile.StudentNumber == number)
            .Select(profile => profile.UserId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AnalysisStatusDto>> GetAnalysisStatusesForStudentAsync(
        string userId,
        IReadOnlyCollection<int> submissionIds,
        CancellationToken cancellationToken = default)
    {
        var ids = submissionIds.Distinct().Take(AnalysisStatusQuery.MaxSubmissionIds).ToList();

        if (ids.Count == 0)
        {
            return [];
        }

        // Ownership in the WHERE clause, as on the detail page: another student's
        // ids simply return nothing.
        return await _context.Submissions
            .AsNoTracking()
            .Where(submission => ids.Contains(submission.Id) && submission.StudentProfile.UserId == userId)
            .Select(submission => new AnalysisStatusDto(submission.Id, submission.AnalysisStatus))
            .ToListAsync(cancellationToken);
    }
}
