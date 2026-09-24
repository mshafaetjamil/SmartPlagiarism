using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.Auditing;
using SmartPlagiarism.Core.DTOs;
using SmartPlagiarism.Core.DTOs.Admin;
using SmartPlagiarism.Core.Entities;
using SmartPlagiarism.Core.Identity;
using SmartPlagiarism.Infrastructure.Data;

namespace SmartPlagiarism.Infrastructure.Admin;

/// <inheritdoc cref="IUserAdministrationService"/>
public class UserAdministrationService : IUserAdministrationService
{
    /// <summary>
    /// Deactivation is a lockout that never ends. Identity already refuses sign-in
    /// to a locked-out account, so no custom sign-in code is needed, and the
    /// sentinel keeps it apart from the short lockouts failed sign-ins cause.
    /// </summary>
    public static readonly DateTimeOffset DeactivatedUntil = DateTimeOffset.MaxValue;

    /// <summary>Student numbers are accepted for students enrolled from this year on.</summary>
    public const int EarliestEnrollmentYear = 2000;

    private readonly AppDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditService _audit;

    public UserAdministrationService(AppDbContext context, UserManager<ApplicationUser> userManager, IAuditService audit)
    {
        _context = context;
        _userManager = userManager;
        _audit = audit;
    }

    public async Task<PagedResult<UserListItemDto>> GetUsersAsync(UserListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var users = _context.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            users = users.Where(user => user.Email!.Contains(term) || user.FullName.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            users = users.Where(user => _context.UserRoles.Any(userRole =>
                userRole.UserId == user.Id
                && _context.Roles.Any(role => role.Id == userRole.RoleId && role.Name == query.Role)));
        }

        if (query.Active is { } active)
        {
            users = active
                ? users.Where(user => user.LockoutEnd != DeactivatedUntil)
                : users.Where(user => user.LockoutEnd == DeactivatedUntil);
        }

        var totalCount = await users.CountAsync(cancellationToken);
        var page = PagedResult<UserListItemDto>.ClampPage(query.Page, totalCount, UserListQuery.PageSize);

        var rows = await users
            .OrderBy(user => user.FullName)
            .ThenBy(user => user.Email)
            .Skip((page - 1) * UserListQuery.PageSize)
            .Take(UserListQuery.PageSize)
            .Select(user => new
            {
                user.Id,
                user.Email,
                user.FullName,
                user.LockoutEnd,
                Role = (from userRole in _context.UserRoles
                        join role in _context.Roles on userRole.RoleId equals role.Id
                        where userRole.UserId == user.Id
                        select role.Name).FirstOrDefault(),
                Student = _context.StudentProfiles
                    .Where(profile => profile.UserId == user.Id)
                    .Select(profile => new { Number = profile.StudentNumber, profile.Department.Code })
                    .FirstOrDefault(),
                Teacher = _context.TeacherProfiles
                    .Where(profile => profile.UserId == user.Id)
                    .Select(profile => new { Number = profile.EmployeeNumber, profile.Department.Code })
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;

        var items = rows.Select(row =>
        {
            var deactivated = row.LockoutEnd == DeactivatedUntil;

            return new UserListItemDto(
                row.Id,
                row.Email ?? string.Empty,
                row.FullName,
                row.Role,
                row.Student?.Number ?? row.Teacher?.Number,
                row.Student?.Code ?? row.Teacher?.Code,
                deactivated,
                !deactivated && row.LockoutEnd > now ? row.LockoutEnd : null);
        }).ToList();

        return new PagedResult<UserListItemDto>(items, page, UserListQuery.PageSize, totalCount);
    }

    public async Task<AdminResult> CreateAsync(CreateUserRequest request, string adminUserId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = await ValidateAsync(request, cancellationToken);

        if (errors.Count > 0)
        {
            return new AdminResult(false, errors);
        }

        var email = request.Email.Trim();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = request.FullName.Trim(),

            // Accounts are made by an administrator; there is no sign-up to confirm.
            EmailConfirmed = true,
        };

        // Identity checks the password policy and that the email is unused.
        var created = await _userManager.CreateAsync(user, request.Password);

        if (!created.Succeeded)
        {
            return new AdminResult(false, [.. created.Errors.Select(error => error.Description)]);
        }

        try
        {
            var inRole = await _userManager.AddToRoleAsync(user, request.Role);

            if (!inRole.Succeeded)
            {
                throw new InvalidOperationException(string.Join(" ", inRole.Errors.Select(error => error.Description)));
            }

            AddProfile(user, request);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            // All or nothing: an account without its role or profile would be
            // unusable, and would block the email from being used again.
            _context.ChangeTracker.Clear();
            await _userManager.DeleteAsync(user);

            return AdminResult.Failure(ex is DbUpdateException
                ? "That student or employee number was taken just now. Try again with another."
                : $"The account could not be set up: {ex.Message}");
        }

        await _audit.RecordAsync(
            new AuditEntry(AuditActions.UserCreated, AuditEntityTypes.User, user.Id, adminUserId, $"{request.Role}: {email}"),
            cancellationToken);

        return AdminResult.Success;
    }

    public async Task<AdminResult> SetActiveAsync(string userId, bool active, string adminUserId, CancellationToken cancellationToken = default)
    {
        if (!active && userId == adminUserId)
        {
            return AdminResult.Failure("You cannot deactivate your own account.");
        }

        var user = await _userManager.FindByIdAsync(userId);

        if (user is null)
        {
            return AdminResult.Failure("That user no longer exists.");
        }

        if (active)
        {
            await _userManager.SetLockoutEndDateAsync(user, null);
            await _userManager.ResetAccessFailedCountAsync(user);
        }
        else
        {
            await _userManager.SetLockoutEnabledAsync(user, true);
            await _userManager.SetLockoutEndDateAsync(user, DeactivatedUntil);

            // A new stamp invalidates the user's existing sign-in cookies at their
            // next validation, so deactivation also ends sessions already open.
            await _userManager.UpdateSecurityStampAsync(user);
        }

        await _audit.RecordAsync(
            new AuditEntry(
                active ? AuditActions.UserReactivated : AuditActions.UserDeactivated,
                AuditEntityTypes.User,
                user.Id,
                adminUserId,
                user.Email),
            cancellationToken);

        return AdminResult.Success;
    }

    private async Task<List<string>> ValidateAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            errors.Add("An email address is required.");
        }

        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            errors.Add("A full name is required.");
        }

        if (!ApplicationRoles.All.Contains(request.Role))
        {
            errors.Add("Choose a role.");
            return errors;
        }

        if (request.Role != ApplicationRoles.Admin)
        {
            errors.AddRange(await ValidateProfileAsync(request, cancellationToken));
        }

        return errors;
    }

    /// <summary>The department and number a student or teacher account needs.</summary>
    private async Task<List<string>> ValidateProfileAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        if (request.DepartmentId is not { } departmentId
            || !await _context.Departments.AnyAsync(department => department.Id == departmentId, cancellationToken))
        {
            errors.Add("Choose a department.");
        }

        var isStudent = request.Role == ApplicationRoles.Student;
        var number = (isStudent ? request.StudentNumber : request.EmployeeNumber)?.Trim();
        var label = isStudent ? "student number" : "employee number";

        if (string.IsNullOrEmpty(number))
        {
            errors.Add($"A {label} is required.");
        }
        else if (number.Length > StudentProfile.MaxNumberLength)
        {
            errors.Add($"The {label} can be at most {StudentProfile.MaxNumberLength} characters.");
        }
        else if (isStudent
            ? await _context.StudentProfiles.AnyAsync(profile => profile.StudentNumber == number, cancellationToken)
            : await _context.TeacherProfiles.AnyAsync(profile => profile.EmployeeNumber == number, cancellationToken))
        {
            errors.Add($"That {label} is already in use.");
        }

        // Next year's intake can be registered ahead of time.
        var latestYear = DateTime.UtcNow.Year + 1;

        if (isStudent && (request.EnrollmentYear is not { } year || year < EarliestEnrollmentYear || year > latestYear))
        {
            errors.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"Enter an enrolment year from {EarliestEnrollmentYear} to {latestYear}."));
        }

        return errors;
    }

    private void AddProfile(ApplicationUser user, CreateUserRequest request)
    {
        if (request.Role == ApplicationRoles.Student)
        {
            _context.StudentProfiles.Add(new StudentProfile
            {
                UserId = user.Id,
                StudentNumber = request.StudentNumber!.Trim(),
                DepartmentId = request.DepartmentId!.Value,
                EnrollmentYear = request.EnrollmentYear!.Value,
            });
        }
        else if (request.Role == ApplicationRoles.Teacher)
        {
            _context.TeacherProfiles.Add(new TeacherProfile
            {
                UserId = user.Id,
                EmployeeNumber = request.EmployeeNumber!.Trim(),
                DepartmentId = request.DepartmentId!.Value,
            });
        }
    }
}
