using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SmartPlagiarism.Core.DTOs;
using SmartPlagiarism.Core.DTOs.Admin;
using SmartPlagiarism.Core.Entities;
using SmartPlagiarism.Core.Identity;

namespace SmartPlagiarism.Web.Models.Admin;

public static class AdminResultExtensions
{
    /// <summary>Shows a refused change's reasons on the form that made it.</summary>
    public static void AddErrors(this ModelStateDictionary modelState, AdminResult result)
    {
        foreach (var error in result.Errors)
        {
            modelState.AddModelError(string.Empty, error);
        }
    }

    /// <summary>
    /// The validation errors as one sentence, for the forms that post from a
    /// list page and report back through its status message.
    /// </summary>
    public static string ErrorSummary(this ModelStateDictionary modelState) =>
        string.Join(" ", modelState.Values.SelectMany(entry => entry.Errors).Select(error => error.ErrorMessage));
}

/// <summary>Builds a query string from the non-empty values, for filter-preserving links.</summary>
internal static class QueryValues
{
    public static Dictionary<string, string> From(params (string Key, object? Value)[] values) =>
        values
            .Where(pair => pair.Value is not null && !(pair.Value is string text && text.Length == 0))
            .ToDictionary(pair => pair.Key, pair => Convert.ToString(pair.Value, CultureInfo.InvariantCulture)!);
}

// ---------------- users ----------------

public sealed class UserListFilter
{
    public string? Search { get; set; }

    public string? Role { get; set; }

    public bool? Active { get; set; }

    public int Page { get; set; } = 1;

    public UserListQuery ToQuery() => new(Search, Role, Active, Page);

    public Dictionary<string, string> RouteValues(int page) =>
        QueryValues.From((nameof(Search), Search), (nameof(Role), Role), (nameof(Active), Active), (nameof(Page), page));
}

public sealed record UserListViewModel(UserListFilter Filter, PagedResult<UserListItemDto> Results, string CurrentUserId);

public sealed class CreateUserViewModel
{
    /// <summary>The length of Identity's email column.</summary>
    private const int MaxEmailLength = 256;

    [Required]
    [EmailAddress]
    [StringLength(MaxEmailLength)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(256)]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = ApplicationRoles.Student;

    [Display(Name = "Department")]
    public int? DepartmentId { get; set; }

    [StringLength(StudentProfile.MaxNumberLength)]
    [Display(Name = "Student number")]
    public string? StudentNumber { get; set; }

    [Range(2000, 2100)]
    [Display(Name = "Enrolment year")]
    public int? EnrollmentYear { get; set; } = DateTime.UtcNow.Year;

    [StringLength(TeacherProfile.MaxNumberLength)]
    [Display(Name = "Employee number")]
    public string? EmployeeNumber { get; set; }

    public IReadOnlyList<DepartmentDto> Departments { get; set; } = [];

    public CreateUserRequest ToRequest() =>
        new(Email, FullName, Password, Role, DepartmentId, StudentNumber, EnrollmentYear, EmployeeNumber);
}

// ---------------- academic structure ----------------

public sealed class DepartmentFormViewModel
{
    public int? Id { get; set; }

    [Required]
    [StringLength(Department.MaxCodeLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(Department.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    public DepartmentInput ToInput() => new(Code, Name);
}

public sealed class CourseFormViewModel
{
    public int? Id { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Choose a department.")]
    [Display(Name = "Department")]
    public int DepartmentId { get; set; }

    [Required]
    [StringLength(Course.MaxCodeLength)]
    public string Code { get; set; } = string.Empty;

    [Required]
    [StringLength(Course.MaxTitleLength)]
    public string Title { get; set; } = string.Empty;

    public IReadOnlyList<DepartmentDto> Departments { get; set; } = [];

    public CourseInput ToInput() => new(DepartmentId, Code, Title);
}

public sealed class SemesterFormViewModel
{
    public int? Id { get; set; }

    [Required]
    [StringLength(Semester.MaxNameLength)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Starts")]
    public DateOnly? StartDate { get; set; }

    [Required]
    [Display(Name = "Ends")]
    public DateOnly? EndDate { get; set; }

    public SemesterInput ToInput() => new(
        Name,
        StartDate!.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
        EndDate!.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
}

// ---------------- audit log ----------------

public sealed class AuditLogFilter
{
    public string? Action { get; set; }

    public string? EntityType { get; set; }

    public string? User { get; set; }

    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }

    public int Page { get; set; } = 1;

    public AuditLogQuery ToQuery() => new(Action, EntityType, User, From, To, Page);

    public Dictionary<string, string> RouteValues(int page) => QueryValues.From(
        (nameof(Action), Action),
        (nameof(EntityType), EntityType),
        (nameof(User), User),
        (nameof(From), From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
        (nameof(To), To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
        (nameof(Page), page));
}

public sealed record AuditLogViewModel(AuditLogFilter Filter, PagedResult<AuditLogEntryDto> Results, AuditLogFilterOptions Options);

// ---------------- API keys ----------------

/// <summary>The one field a new key needs: whom it is for.</summary>
public sealed class ApiKeyFormViewModel
{
    [Required(ErrorMessage = "Give the key a name, such as the system that will use it.")]
    [StringLength(ApiKey.MaxNameLength)]
    public string Name { get; set; } = string.Empty;
}
