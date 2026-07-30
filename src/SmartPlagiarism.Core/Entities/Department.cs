namespace SmartPlagiarism.Core.Entities;

/// <summary>An academic department, e.g. Computer Science and Engineering.</summary>
public class Department
{
    public int Id { get; set; }

    /// <summary>Short code used in the UI and in student numbers, e.g. "CSE".</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public ICollection<Course> Courses { get; } = [];
}
