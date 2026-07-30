using SmartPlagiarism.Core.Enums;
using SmartPlagiarism.Core.Files;

namespace SmartPlagiarism.Core.DTOs.Submissions;

/// <param name="StudentUserId">Identity user id of the signed-in student.</param>
public sealed record CreateSubmissionRequest(
    string StudentUserId,
    string Title,
    SubmissionType Type,
    int CourseId,
    int SemesterId,
    IReadOnlyList<FileUpload> Files);

/// <summary>
/// Outcome of a create attempt. Failures carry every problem found, so the student
/// fixes them in one pass instead of one error at a time.
/// </summary>
public sealed record CreateSubmissionResult(bool Succeeded, int SubmissionId, IReadOnlyList<string> Errors)
{
    public static CreateSubmissionResult Success(int submissionId) => new(true, submissionId, []);

    public static CreateSubmissionResult Failure(IReadOnlyList<string> errors) => new(false, 0, errors);

    public static CreateSubmissionResult Failure(string error) => new(false, 0, [error]);
}
