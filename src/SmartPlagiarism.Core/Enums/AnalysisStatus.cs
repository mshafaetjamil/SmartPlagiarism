namespace SmartPlagiarism.Core.Enums;

/// <summary>
/// Where a submission sits in the background analysis pipeline. The UI polls
/// this while a submission is Queued or Processing.
/// </summary>
public enum AnalysisStatus
{
    Queued = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3,
}
