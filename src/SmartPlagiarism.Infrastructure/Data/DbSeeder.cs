using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartPlagiarism.Core.Entities;
using SmartPlagiarism.Core.Identity;

namespace SmartPlagiarism.Infrastructure.Data;

/// <summary>
/// Creates the roles, demo accounts and demo academic structure described in
/// CLAUDE.md. Idempotent: running it against an already-seeded database is a
/// no-op, so it is safe to call on every Development startup.
/// </summary>
public static class DbSeeder
{
    private const string AdminEmail = "admin@uni.edu";
    private const string TeacherEmail = "teacher@uni.edu";
    private const string StudentEmail = "student@uni.edu";

    private const string DemoDepartmentCode = "CSE";

    private sealed record DemoUser(string Email, string Password, string FullName, string Role);

    private sealed record DemoCourse(string Code, string Title);

    private static readonly DemoUser[] s_demoUsers =
    [
        new(AdminEmail, "Admin#123", "System Administrator", ApplicationRoles.Admin),
        new(TeacherEmail, "Teacher#123", "Demo Teacher", ApplicationRoles.Teacher),
        new(StudentEmail, "Student#123", "Demo Student", ApplicationRoles.Student),
    ];

    private static readonly DemoCourse[] s_demoCourses =
    [
        new("CSE401", "Software Engineering"),
        new("CSE402", "Final Year Project"),
    ];

    /// <summary>
    /// Applies pending migrations, then seeds roles, demo users, the demo academic
    /// structure, and the profiles that link the two together.
    /// </summary>
    /// <param name="services">A scoped service provider.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var context = services.GetRequiredService<AppDbContext>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DbSeeder));

        await context.Database.MigrateAsync(cancellationToken);

        await SeedRolesAsync(roleManager, logger);
        await SeedUsersAsync(userManager, logger);

        var department = await SeedAcademicStructureAsync(context, logger, cancellationToken);
        await SeedProfilesAsync(context, userManager, department, logger, cancellationToken);
    }

    private static async Task SeedRolesAsync(RoleManager<IdentityRole> roleManager, ILogger logger)
    {
        foreach (var role in ApplicationRoles.All)
        {
            if (await roleManager.RoleExistsAsync(role))
            {
                continue;
            }

            ThrowIfFailed(await roleManager.CreateAsync(new IdentityRole(role)), $"create role '{role}'");
            logger.LogInformation("Seeded role {Role}.", role);
        }
    }

    private static async Task SeedUsersAsync(UserManager<ApplicationUser> userManager, ILogger logger)
    {
        foreach (var demoUser in s_demoUsers)
        {
            var user = await userManager.FindByEmailAsync(demoUser.Email);

            if (user is null)
            {
                user = new ApplicationUser
                {
                    UserName = demoUser.Email,
                    Email = demoUser.Email,
                    FullName = demoUser.FullName,

                    // Accounts are created by an administrator rather than by public
                    // signup, so there is no confirmation mail to wait for.
                    EmailConfirmed = true,
                };

                ThrowIfFailed(
                    await userManager.CreateAsync(user, demoUser.Password),
                    $"create user '{demoUser.Email}'");

                logger.LogInformation("Seeded {Role} account {Email}.", demoUser.Role, demoUser.Email);
            }

            if (!await userManager.IsInRoleAsync(user, demoUser.Role))
            {
                ThrowIfFailed(
                    await userManager.AddToRoleAsync(user, demoUser.Role),
                    $"assign role '{demoUser.Role}' to '{demoUser.Email}'");
            }
        }
    }

    private static async Task<Department> SeedAcademicStructureAsync(
        AppDbContext context,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var department = await context.Departments
            .FirstOrDefaultAsync(candidate => candidate.Code == DemoDepartmentCode, cancellationToken);

        if (department is null)
        {
            department = new Department
            {
                Code = DemoDepartmentCode,
                Name = "Computer Science and Engineering",
            };

            context.Departments.Add(department);
            await context.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded department {Code}.", department.Code);
        }

        foreach (var demoCourse in s_demoCourses)
        {
            var exists = await context.Courses.AnyAsync(
                candidate => candidate.DepartmentId == department.Id && candidate.Code == demoCourse.Code,
                cancellationToken);

            if (exists)
            {
                continue;
            }

            context.Courses.Add(new Course
            {
                DepartmentId = department.Id,
                Code = demoCourse.Code,
                Title = demoCourse.Title,
            });

            logger.LogInformation("Seeded course {Code}.", demoCourse.Code);
        }

        if (!await context.Semesters.AnyAsync(cancellationToken))
        {
            context.Semesters.Add(new Semester
            {
                Name = "Summer 2026",
                StartDateUtc = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDateUtc = new DateTime(2026, 9, 30, 23, 59, 59, DateTimeKind.Utc),
            });

            logger.LogInformation("Seeded semester Summer 2026.");
        }

        await context.SaveChangesAsync(cancellationToken);
        return department;
    }

    /// <summary>
    /// Gives the demo teacher and student the profiles their roles require. Without
    /// these the seeded accounts cannot own a submission, so the demo data would be
    /// unusable from Phase 3 onwards. The admin account gets no profile: an
    /// administrator is neither a student nor a teacher.
    /// </summary>
    private static async Task SeedProfilesAsync(
        AppDbContext context,
        UserManager<ApplicationUser> userManager,
        Department department,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var student = await userManager.FindByEmailAsync(StudentEmail);

        if (student is not null
            && !await context.StudentProfiles.AnyAsync(profile => profile.UserId == student.Id, cancellationToken))
        {
            context.StudentProfiles.Add(new StudentProfile
            {
                UserId = student.Id,
                StudentNumber = "2026-CSE-001",
                DepartmentId = department.Id,
                EnrollmentYear = 2026,
            });

            logger.LogInformation("Seeded student profile for {Email}.", StudentEmail);
        }

        var teacher = await userManager.FindByEmailAsync(TeacherEmail);

        if (teacher is not null
            && !await context.TeacherProfiles.AnyAsync(profile => profile.UserId == teacher.Id, cancellationToken))
        {
            context.TeacherProfiles.Add(new TeacherProfile
            {
                UserId = teacher.Id,
                EmployeeNumber = "T-1042",
                DepartmentId = department.Id,
            });

            logger.LogInformation("Seeded teacher profile for {Email}.", TeacherEmail);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static void ThrowIfFailed(IdentityResult result, string operation)
    {
        if (result.Succeeded)
        {
            return;
        }

        var errors = string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Description}"));
        throw new InvalidOperationException($"Database seeding failed - could not {operation}. {errors}");
    }
}
