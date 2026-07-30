namespace SmartPlagiarism.Core.Entities;

/// <summary>A course offered by a department. Submissions are made against a course.</summary>
public class Course
{
    public int Id { get; set; }

    public int DepartmentId { get; set; }

    public Department Department { get; set; } = null!;

    /// <summary>Course code, unique within its department, e.g. "CSE401".</summary>
    public string Code { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
}
