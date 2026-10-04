using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.Auditing;
using SmartPlagiarism.Core.DTOs;
using SmartPlagiarism.Core.DTOs.Admin;
using SmartPlagiarism.Core.Entities;
using SmartPlagiarism.Infrastructure.Data;

namespace SmartPlagiarism.Infrastructure.Admin;

/// <inheritdoc cref="IAcademicStructureService"/>
/// <remarks>
/// Uniqueness is checked up front for a readable message, and again by the
/// database's unique indexes, whose violation - two admins racing - is turned
/// into the same message rather than an error page.
/// </remarks>
public class AcademicStructureService : IAcademicStructureService
{
    private readonly AppDbContext _context;
    private readonly IAuditService _audit;

    public AcademicStructureService(AppDbContext context, IAuditService audit)
    {
        _context = context;
        _audit = audit;
    }

    // ---------------- departments ----------------

    public async Task<IReadOnlyList<DepartmentDto>> GetDepartmentsAsync(CancellationToken cancellationToken = default) =>
        await _context.Departments
            .AsNoTracking()
            .OrderBy(department => department.Code)
            .Select(department => new DepartmentDto(department.Id, department.Code, department.Name, department.Courses.Count))
            .ToListAsync(cancellationToken);

    public Task<PagedResult<DepartmentDto>> GetDepartmentsAsync(int page, CancellationToken cancellationToken = default) =>
        _context.Departments
            .AsNoTracking()
            .OrderBy(department => department.Code)
            .ThenBy(department => department.Id)
            .Select(department => new DepartmentDto(department.Id, department.Code, department.Name, department.Courses.Count))
            .ToPagedResultAsync(page, Paging.DefaultPageSize, cancellationToken);

    public async Task<DepartmentDto?> GetDepartmentAsync(int id, CancellationToken cancellationToken = default) =>
        await _context.Departments
            .AsNoTracking()
            .Where(department => department.Id == id)
            .Select(department => new DepartmentDto(department.Id, department.Code, department.Name, department.Courses.Count))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<AdminResult> CreateDepartmentAsync(DepartmentInput input, string adminUserId, CancellationToken cancellationToken = default)
    {
        var department = new Department();
        return await SaveDepartmentAsync(department, input, adminUserId, isNew: true, cancellationToken);
    }

    public async Task<AdminResult> UpdateDepartmentAsync(int id, DepartmentInput input, string adminUserId, CancellationToken cancellationToken = default)
    {
        var department = await _context.Departments.FindAsync([id], cancellationToken);

        return department is null
            ? AdminResult.Failure("That department no longer exists.")
            : await SaveDepartmentAsync(department, input, adminUserId, isNew: false, cancellationToken);
    }

    public async Task<AdminResult> DeleteDepartmentAsync(int id, string adminUserId, CancellationToken cancellationToken = default)
    {
        var department = await _context.Departments.FindAsync([id], cancellationToken);

        if (department is null)
        {
            return AdminResult.Failure("That department no longer exists.");
        }

        var inUse = await _context.Courses.AnyAsync(course => course.DepartmentId == id, cancellationToken)
            || await _context.StudentProfiles.AnyAsync(profile => profile.DepartmentId == id, cancellationToken)
            || await _context.TeacherProfiles.AnyAsync(profile => profile.DepartmentId == id, cancellationToken);

        if (inUse)
        {
            return AdminResult.Failure(
                $"{department.Code} still has courses, students or teachers. Move or remove them first.");
        }

        _context.Departments.Remove(department);
        return await SaveAsync(AuditActions.RecordDeleted, AuditEntityTypes.Department, id, $"{department.Code} {department.Name}", adminUserId, cancellationToken);
    }

    private async Task<AdminResult> SaveDepartmentAsync(
        Department department,
        DepartmentInput input,
        string adminUserId,
        bool isNew,
        CancellationToken cancellationToken)
    {
        var code = input.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        var name = input.Name?.Trim() ?? string.Empty;

        var errors = Required(("Code", code, Department.MaxCodeLength), ("Name", name, Department.MaxNameLength));

        if (errors.Count == 0 && await _context.Departments.AnyAsync(
                other => other.Id != department.Id && (other.Code == code || other.Name == name),
                cancellationToken))
        {
            errors.Add("Another department already has that code or name.");
        }

        if (errors.Count > 0)
        {
            return new AdminResult(false, errors);
        }

        department.Code = code;
        department.Name = name;

        if (isNew)
        {
            _context.Departments.Add(department);
        }

        return await SaveAsync(isNew ? AuditActions.RecordCreated : AuditActions.RecordUpdated, AuditEntityTypes.Department, department, $"{code} {name}", adminUserId, cancellationToken);
    }

    // ---------------- courses ----------------

    public Task<PagedResult<CourseDto>> GetCoursesAsync(int page, CancellationToken cancellationToken = default) =>
        _context.Courses
            .AsNoTracking()
            .OrderBy(course => course.Department.Code)
            .ThenBy(course => course.Code)
            .ThenBy(course => course.Id)
            .Select(CourseProjection)
            .ToPagedResultAsync(page, Paging.DefaultPageSize, cancellationToken);

    public async Task<CourseDto?> GetCourseAsync(int id, CancellationToken cancellationToken = default) =>
        await _context.Courses
            .AsNoTracking()
            .Where(course => course.Id == id)
            .Select(CourseProjection)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<AdminResult> CreateCourseAsync(CourseInput input, string adminUserId, CancellationToken cancellationToken = default) =>
        await SaveCourseAsync(new Course(), input, adminUserId, isNew: true, cancellationToken);

    public async Task<AdminResult> UpdateCourseAsync(int id, CourseInput input, string adminUserId, CancellationToken cancellationToken = default)
    {
        var course = await _context.Courses.FindAsync([id], cancellationToken);

        return course is null
            ? AdminResult.Failure("That course no longer exists.")
            : await SaveCourseAsync(course, input, adminUserId, isNew: false, cancellationToken);
    }

    public async Task<AdminResult> DeleteCourseAsync(int id, string adminUserId, CancellationToken cancellationToken = default)
    {
        var course = await _context.Courses.FindAsync([id], cancellationToken);

        if (course is null)
        {
            return AdminResult.Failure("That course no longer exists.");
        }

        if (await _context.Submissions.AnyAsync(submission => submission.CourseId == id, cancellationToken))
        {
            return AdminResult.Failure($"{course.Code} has submissions against it and cannot be deleted.");
        }

        _context.Courses.Remove(course);
        return await SaveAsync(AuditActions.RecordDeleted, AuditEntityTypes.Course, id, $"{course.Code} {course.Title}", adminUserId, cancellationToken);
    }

    /// <summary>
    /// Applied last, after any ordering or filtering: EF cannot translate members
    /// of a record built by its constructor, so nothing may query the DTO itself.
    /// </summary>
    private Expression<Func<Course, CourseDto>> CourseProjection => course => new CourseDto(
        course.Id,
        course.DepartmentId,
        course.Department.Code,
        course.Code,
        course.Title,
        _context.Submissions.Count(submission => submission.CourseId == course.Id));

    private async Task<AdminResult> SaveCourseAsync(
        Course course,
        CourseInput input,
        string adminUserId,
        bool isNew,
        CancellationToken cancellationToken)
    {
        var code = input.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        var title = input.Title?.Trim() ?? string.Empty;

        var errors = Required(("Code", code, Course.MaxCodeLength), ("Title", title, Course.MaxTitleLength));

        if (!await _context.Departments.AnyAsync(department => department.Id == input.DepartmentId, cancellationToken))
        {
            errors.Add("Choose a department.");
        }
        else if (errors.Count == 0 && await _context.Courses.AnyAsync(
                     other => other.Id != course.Id && other.DepartmentId == input.DepartmentId && other.Code == code,
                     cancellationToken))
        {
            errors.Add("That department already has a course with this code.");
        }

        if (errors.Count > 0)
        {
            return new AdminResult(false, errors);
        }

        course.DepartmentId = input.DepartmentId;
        course.Code = code;
        course.Title = title;

        if (isNew)
        {
            _context.Courses.Add(course);
        }

        return await SaveAsync(isNew ? AuditActions.RecordCreated : AuditActions.RecordUpdated, AuditEntityTypes.Course, course, $"{code} {title}", adminUserId, cancellationToken);
    }

    // ---------------- semesters ----------------

    public Task<PagedResult<SemesterDto>> GetSemestersAsync(int page, CancellationToken cancellationToken = default) =>
        _context.Semesters
            .AsNoTracking()
            .OrderByDescending(semester => semester.StartDateUtc)
            .ThenByDescending(semester => semester.Id)
            .Select(SemesterProjection)
            .ToPagedResultAsync(page, Paging.DefaultPageSize, cancellationToken);

    public async Task<SemesterDto?> GetSemesterAsync(int id, CancellationToken cancellationToken = default) =>
        await _context.Semesters
            .AsNoTracking()
            .Where(semester => semester.Id == id)
            .Select(SemesterProjection)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<AdminResult> CreateSemesterAsync(SemesterInput input, string adminUserId, CancellationToken cancellationToken = default) =>
        await SaveSemesterAsync(new Semester(), input, adminUserId, isNew: true, cancellationToken);

    public async Task<AdminResult> UpdateSemesterAsync(int id, SemesterInput input, string adminUserId, CancellationToken cancellationToken = default)
    {
        var semester = await _context.Semesters.FindAsync([id], cancellationToken);

        return semester is null
            ? AdminResult.Failure("That semester no longer exists.")
            : await SaveSemesterAsync(semester, input, adminUserId, isNew: false, cancellationToken);
    }

    public async Task<AdminResult> DeleteSemesterAsync(int id, string adminUserId, CancellationToken cancellationToken = default)
    {
        var semester = await _context.Semesters.FindAsync([id], cancellationToken);

        if (semester is null)
        {
            return AdminResult.Failure("That semester no longer exists.");
        }

        if (await _context.Submissions.AnyAsync(submission => submission.SemesterId == id, cancellationToken))
        {
            return AdminResult.Failure($"{semester.Name} has submissions against it and cannot be deleted.");
        }

        _context.Semesters.Remove(semester);
        return await SaveAsync(AuditActions.RecordDeleted, AuditEntityTypes.Semester, id, semester.Name, adminUserId, cancellationToken);
    }

    /// <inheritdoc cref="CourseProjection"/>
    private Expression<Func<Semester, SemesterDto>> SemesterProjection => semester => new SemesterDto(
        semester.Id,
        semester.Name,
        semester.StartDateUtc,
        semester.EndDateUtc,
        _context.Submissions.Count(submission => submission.SemesterId == semester.Id));

    private async Task<AdminResult> SaveSemesterAsync(
        Semester semester,
        SemesterInput input,
        string adminUserId,
        bool isNew,
        CancellationToken cancellationToken)
    {
        var name = input.Name?.Trim() ?? string.Empty;
        var errors = Required(("Name", name, Semester.MaxNameLength));

        if (input.EndDateUtc <= input.StartDateUtc)
        {
            errors.Add("The semester must end after it starts.");
        }

        if (errors.Count == 0 && await _context.Semesters.AnyAsync(other => other.Id != semester.Id && other.Name == name, cancellationToken))
        {
            errors.Add("Another semester already has that name.");
        }

        if (errors.Count > 0)
        {
            return new AdminResult(false, errors);
        }

        semester.Name = name;
        semester.StartDateUtc = DateTime.SpecifyKind(input.StartDateUtc.Date, DateTimeKind.Utc);
        semester.EndDateUtc = DateTime.SpecifyKind(input.EndDateUtc.Date, DateTimeKind.Utc);

        if (isNew)
        {
            _context.Semesters.Add(semester);
        }

        return await SaveAsync(isNew ? AuditActions.RecordCreated : AuditActions.RecordUpdated, AuditEntityTypes.Semester, semester, name, adminUserId, cancellationToken);
    }

    // ---------------- shared ----------------

    private static List<string> Required(params (string Field, string Value, int MaxLength)[] fields)
    {
        var errors = new List<string>();

        foreach (var (field, value, maxLength) in fields)
        {
            if (value.Length == 0)
            {
                errors.Add($"{field} is required.");
            }
            else if (value.Length > maxLength)
            {
                errors.Add(string.Create(CultureInfo.InvariantCulture, $"{field} can be at most {maxLength} characters."));
            }
        }

        return errors;
    }

    /// <summary>Saves, then audits against the id the save assigned.</summary>
    private Task<AdminResult> SaveAsync<TEntity>(
        string action,
        string entityType,
        TEntity entity,
        string details,
        string adminUserId,
        CancellationToken cancellationToken)
        where TEntity : class =>
        SaveAsync(action, entityType, () => _context.Entry(entity).Property<int>("Id").CurrentValue, details, adminUserId, cancellationToken);

    private Task<AdminResult> SaveAsync(
        string action,
        string entityType,
        int id,
        string details,
        string adminUserId,
        CancellationToken cancellationToken) =>
        SaveAsync(action, entityType, () => id, details, adminUserId, cancellationToken);

    private async Task<AdminResult> SaveAsync(
        string action,
        string entityType,
        Func<int> id,
        string details,
        string adminUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The database caught what the checks above could not, because someone
            // else changed things in the meantime: a duplicate code saved, or a
            // reference added to a record being deleted.
            _context.ChangeTracker.Clear();
            return AdminResult.Failure(
                "That change conflicts with one someone else just made (a duplicate code or name, or a new reference). Reload and try again.");
        }

        await _audit.RecordAsync(
            new AuditEntry(action, entityType, id().ToString(CultureInfo.InvariantCulture), adminUserId, details),
            cancellationToken);

        return AdminResult.Success;
    }
}
