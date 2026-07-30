using System.ComponentModel.DataAnnotations;
using SmartPlagiarism.Core.DTOs.Submissions;
using SmartPlagiarism.Core.Enums;

namespace SmartPlagiarism.Web.Models.Submissions;

public class CreateSubmissionViewModel
{
    [Required]
    [StringLength(250, MinimumLength = 3)]
    [Display(Name = "Title")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "Type")]
    public SubmissionType Type { get; set; } = SubmissionType.Report;

    [Range(1, int.MaxValue, ErrorMessage = "Select a course.")]
    [Display(Name = "Course")]
    public int CourseId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Select a semester.")]
    [Display(Name = "Semester")]
    public int SemesterId { get; set; }

    [Display(Name = "Files")]
    public List<IFormFile> Files { get; set; } = [];

    /// <summary>Dropdown contents, refilled by the controller whenever the form is redisplayed.</summary>
    public IReadOnlyList<CourseOptionDto> Courses { get; set; } = [];

    public IReadOnlyList<SemesterOptionDto> Semesters { get; set; } = [];
}
