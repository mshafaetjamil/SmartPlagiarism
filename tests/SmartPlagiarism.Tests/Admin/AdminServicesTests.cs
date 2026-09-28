using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.Auditing;
using SmartPlagiarism.Core.DTOs.Admin;
using SmartPlagiarism.Core.Enums;
using SmartPlagiarism.Core.Identity;
using SmartPlagiarism.Core.Review;
using SmartPlagiarism.Infrastructure.Admin;
using SmartPlagiarism.Infrastructure.Data;
using SmartPlagiarism.Infrastructure.Services;
using SmartPlagiarism.Tests.Analysis;
using SmartPlagiarism.Tests.Extraction;

namespace SmartPlagiarism.Tests.Admin;

/// <summary>
/// The admin module's services over a real database with real Identity: account
/// creation all-or-nothing, deactivation, the academic structure's integrity
/// rules, the statistics and the audit log reader.
/// </summary>
public sealed class AdminServicesTests : IAsyncLifetime
{
    private const string Admin = "admin-user-id";
    private const string StrongPassword = "Student#2026";

    private AnalysisTestHarness _harness = null!;
    private ServiceProvider _provider = null!;
    private AsyncServiceScope _scope;

    private AppDbContext Context => _scope.ServiceProvider.GetRequiredService<AppDbContext>();

    private IUserAdministrationService Users => _scope.ServiceProvider.GetRequiredService<IUserAdministrationService>();

    private IAcademicStructureService Academic => _scope.ServiceProvider.GetRequiredService<IAcademicStructureService>();

    public async Task InitializeAsync()
    {
        _harness = await AnalysisTestHarness.CreateAsync();

        var services = new ServiceCollection().AddLogging();
        services.AddDbContext<AppDbContext>(_harness.UseDatabase);
        services.AddIdentityCore<ApplicationUser>(options => options.User.RequireUniqueEmail = true)
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IUserAdministrationService, UserAdministrationService>();
        services.AddScoped<IAcademicStructureService, AcademicStructureService>();

        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateAsyncScope();

        var roles = _scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        foreach (var role in ApplicationRoles.All)
        {
            await roles.CreateAsync(new IdentityRole(role));
        }
    }

    public async Task DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
        await _harness.DisposeAsync();
    }

    private async Task<int> DepartmentIdAsync() =>
        await Context.Departments.Where(department => department.Code == "CSE").Select(department => department.Id).SingleAsync();

    private async Task<CreateUserRequest> StudentAsync(string email = "new.student@uni.edu", string number = "2026-CSE-777") =>
        new(email, "New Student", StrongPassword, ApplicationRoles.Student, await DepartmentIdAsync(), number, 2026, null);

    // ---------------- users ----------------

    [Fact]
    public async Task CreatingAStudent_MakesTheAccountRoleAndProfile_AndAudits()
    {
        var result = await Users.CreateAsync(await StudentAsync(), Admin);

        result.Succeeded.Should().BeTrue(string.Join("; ", result.Errors));

        var user = await Context.Users.SingleAsync(candidate => candidate.Email == "new.student@uni.edu");
        var userManager = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        (await userManager.IsInRoleAsync(user, ApplicationRoles.Student)).Should().BeTrue();
        (await Context.StudentProfiles.SingleAsync(profile => profile.UserId == user.Id)).StudentNumber.Should().Be("2026-CSE-777");
        (await Context.AuditLogs.SingleAsync()).Action.Should().Be(AuditActions.UserCreated);
    }

    [Theory]
    [InlineData(null, 2026, "student number is required")]
    [InlineData("2026-CSE-001", 2026, "already in use")]
    [InlineData("2026-CSE-999", 1850, "enrolment year")]
    public async Task InvalidStudentDetails_CreateNothing(string? number, int year, string expectedError)
    {
        // The harness already has a student numbered 2026-001; add one with the
        // number the "already in use" case collides with.
        await Users.CreateAsync(await StudentAsync("existing@uni.edu", "2026-CSE-001"), Admin);
        var usersBefore = await Context.Users.CountAsync();

        var request = await StudentAsync() with { StudentNumber = number, EnrollmentYear = year };
        var result = await Users.CreateAsync(request, Admin);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains(expectedError, StringComparison.OrdinalIgnoreCase));
        (await Context.Users.CountAsync()).Should().Be(usersBefore);
    }

    [Fact]
    public async Task AWeakPassword_IsRefusedByIdentity_AndNothingIsCreated()
    {
        var result = await Users.CreateAsync(await StudentAsync() with { Password = "short" }, Admin);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
        (await Context.Users.AnyAsync(user => user.Email == "new.student@uni.edu")).Should().BeFalse();
    }

    [Fact]
    public async Task CreatingAnAdmin_NeedsNoProfile()
    {
        var result = await Users.CreateAsync(
            new CreateUserRequest("second.admin@uni.edu", "Second Admin", "Admin#2026", ApplicationRoles.Admin, null, null, null, null),
            Admin);

        result.Succeeded.Should().BeTrue(string.Join("; ", result.Errors));
    }

    [Fact]
    public async Task Deactivating_LocksTheAccountOut_AndReactivatingRestoresIt()
    {
        await Users.CreateAsync(await StudentAsync(), Admin);
        var user = await Context.Users.SingleAsync(candidate => candidate.Email == "new.student@uni.edu");
        var userManager = _scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        (await Users.SetActiveAsync(user.Id, active: false, Admin)).Succeeded.Should().BeTrue();

        (await userManager.IsLockedOutAsync(user)).Should().BeTrue("Identity refuses sign-in to a locked-out account");
        var deactivated = await Users.GetUsersAsync(new UserListQuery(Active: false));
        deactivated.Items.Should().ContainSingle().Which.IsDeactivated.Should().BeTrue();

        (await Users.SetActiveAsync(user.Id, active: true, Admin)).Succeeded.Should().BeTrue();

        (await userManager.IsLockedOutAsync(user)).Should().BeFalse();
        (await Users.GetUsersAsync(new UserListQuery(Active: false))).Items.Should().BeEmpty();
        (await Context.AuditLogs.Select(log => log.Action).ToListAsync())
            .Should().Contain([AuditActions.UserDeactivated, AuditActions.UserReactivated]);
    }

    [Fact]
    public async Task AnAdmin_CannotDeactivateThemselves()
    {
        var result = await Users.SetActiveAsync(Admin, active: false, Admin);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain("your own account");
    }

    [Fact]
    public async Task UserList_SearchesAndFiltersByRole()
    {
        await Users.CreateAsync(await StudentAsync(), Admin);
        await Users.CreateAsync(
            new CreateUserRequest("a.teacher@uni.edu", "Ayesha Teacher", "Teacher#2026", ApplicationRoles.Teacher, await DepartmentIdAsync(), null, null, "EMP-42"),
            Admin);

        var teachers = await Users.GetUsersAsync(new UserListQuery(Role: ApplicationRoles.Teacher));
        var searched = await Users.GetUsersAsync(new UserListQuery(Search: "Ayesha"));

        teachers.Items.Should().OnlyContain(user => user.Role == ApplicationRoles.Teacher);
        teachers.Items.Should().ContainSingle(user => user.Email == "a.teacher@uni.edu").Which.Should().BeEquivalentTo(new
        {
            Email = "a.teacher@uni.edu",
            Role = ApplicationRoles.Teacher,
            Identifier = "EMP-42",
            DepartmentCode = "CSE",
            IsDeactivated = false,
        });
        searched.Items.Should().ContainSingle().Which.FullName.Should().Be("Ayesha Teacher");
    }

    // ---------------- academic structure ----------------

    [Fact]
    public async Task Departments_RejectDuplicates_AndCannotBeDeletedWhileInUse()
    {
        (await Academic.CreateDepartmentAsync(new DepartmentInput("eee", "Electrical Engineering"), Admin)).Succeeded.Should().BeTrue();

        var eee = (await Academic.GetDepartmentsAsync()).Single(department => department.Code == "EEE");
        eee.Code.Should().Be("EEE", "codes are stored upper-case");

        (await Academic.CreateDepartmentAsync(new DepartmentInput("EEE", "Something Else"), Admin)).Succeeded.Should().BeFalse();

        var cse = await DepartmentIdAsync();
        var inUse = await Academic.DeleteDepartmentAsync(cse, Admin);
        inUse.Succeeded.Should().BeFalse();
        inUse.Errors.Single().Should().Contain("still has");

        (await Academic.DeleteDepartmentAsync(eee.Id, Admin)).Succeeded.Should().BeTrue();
        (await Context.AuditLogs.CountAsync(log => log.EntityType == AuditEntityTypes.Department)).Should().Be(2);
    }

    [Fact]
    public async Task Courses_AreUniquePerDepartment_AndCannotBeDeletedWithSubmissions()
    {
        await _harness.SubmitAsync(("report.docx", DocxFixture.Create(["Some text for a submission."])));
        var department = await DepartmentIdAsync();

        (await Academic.CreateCourseAsync(new CourseInput(department, "CSE-500", "Thesis"), Admin)).Succeeded.Should().BeTrue();
        (await Academic.CreateCourseAsync(new CourseInput(department, "cse-500", "Duplicate"), Admin)).Succeeded.Should().BeFalse();
        (await Academic.CreateCourseAsync(new CourseInput(999, "CSE-501", "Nowhere"), Admin)).Errors.Should().Contain("Choose a department.");

        var used = (await Academic.GetCoursesAsync()).Single(course => course.SubmissionCount > 0);
        (await Academic.DeleteCourseAsync(used.Id, Admin)).Succeeded.Should().BeFalse();

        var unused = (await Academic.GetCoursesAsync()).Single(course => course.Code == "CSE-500");
        (await Academic.UpdateCourseAsync(unused.Id, new CourseInput(department, "CSE-500", "Thesis II"), Admin)).Succeeded.Should().BeTrue();
        (await Academic.GetCourseAsync(unused.Id))!.Title.Should().Be("Thesis II");
        (await Academic.DeleteCourseAsync(unused.Id, Admin)).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Semesters_MustEndAfterTheyStart()
    {
        var backwards = await Academic.CreateSemesterAsync(
            new SemesterInput("Spring 2027", new DateTime(2027, 6, 1), new DateTime(2027, 1, 1)), Admin);

        backwards.Succeeded.Should().BeFalse();
        backwards.Errors.Should().Contain("The semester must end after it starts.");

        (await Academic.CreateSemesterAsync(
            new SemesterInput("Spring 2027", new DateTime(2027, 1, 1), new DateTime(2027, 6, 1)), Admin)).Succeeded.Should().BeTrue();

        var spring = (await Academic.GetSemestersAsync()).Single(semester => semester.Name == "Spring 2027");
        spring.StartDateUtc.Should().Be(new DateTime(2027, 1, 1));
        spring.EndDateUtc.Should().Be(new DateTime(2027, 6, 1));
    }

    // ---------------- statistics ----------------

    [Fact]
    public async Task Statistics_SummariseSubmissionsMethodsBandsAndFailures()
    {
        const string shared =
            "Winnowing selects the minimum hash value within each sliding window of consecutive fingerprints, "
            + "which guarantees that any sufficiently long shared passage between two documents will be found.";

        var original = await _harness.SubmitAsync(("original.docx", DocxFixture.Create([shared])));
        var copy = await _harness.SubmitAsOtherStudentAsync(("copy.docx", DocxFixture.Create([shared])));
        var empty = await _harness.SubmitAsync(("empty.docx", DocxFixture.Create([])));

        foreach (var id in new[] { original, copy, empty })
        {
            await _harness.AnalyzeAsync(id);
        }

        var statistics = await new StatisticsService(Context).GetStatisticsAsync(months: 3);

        statistics.TotalSubmissions.Should().Be(3);
        statistics.AnalysedSubmissions.Should().Be(2);
        statistics.SubmissionsByMonth.Should().HaveCount(3);
        statistics.SubmissionsByMonth[^1].Count.Should().Be(3, "all three were submitted this month");
        statistics.ExtractionMethods.Single(method => method.Method == ExtractionMethod.Native).FileCount.Should().Be(3);
        statistics.SimilarityBands.Single(band => band.Band == SimilarityBand.Low).Count.Should().Be(1);
        statistics.SimilarityBands.Single(band => band.Band == SimilarityBand.High).Count.Should().Be(1);
        statistics.FailedAnalyses.Should().ContainSingle().Which.SubmissionId.Should().Be(empty);
    }

    // ---------------- audit log ----------------

    [Fact]
    public async Task AuditLog_FiltersByActionEntityUserAndDate_AndPages()
    {
        await Users.CreateAsync(await StudentAsync(), Admin);
        await Academic.CreateDepartmentAsync(new DepartmentInput("EEE", "Electrical Engineering"), Admin);
        await Academic.CreateDepartmentAsync(new DepartmentInput("ME", "Mechanical Engineering"), Admin);

        var reader = new AuditLogReader(Context);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        (await reader.GetEntriesAsync(new AuditLogQuery())).TotalCount.Should().Be(3);
        (await reader.GetEntriesAsync(new AuditLogQuery(Action: AuditActions.UserCreated))).Items.Should().ContainSingle();
        (await reader.GetEntriesAsync(new AuditLogQuery(EntityType: AuditEntityTypes.Department))).TotalCount.Should().Be(2);
        (await reader.GetEntriesAsync(new AuditLogQuery(FromDate: today, ToDate: today))).TotalCount.Should().Be(3);
        (await reader.GetEntriesAsync(new AuditLogQuery(ToDate: today.AddDays(-1)))).TotalCount.Should().Be(0);

        var options = await reader.GetFilterOptionsAsync();
        options.Actions.Should().BeEquivalentTo([AuditActions.UserCreated, AuditActions.RecordCreated]);
        options.EntityTypes.Should().BeEquivalentTo([AuditEntityTypes.User, AuditEntityTypes.Department]);
    }

    [Fact]
    public async Task AuditLog_ShowsWhoActed_ByEmail()
    {
        await Users.CreateAsync(await StudentAsync(), Admin);
        var student = await Context.Users.SingleAsync(user => user.Email == "new.student@uni.edu");

        // The student acting, so the entry has a user whose email can be joined in.
        await _scope.ServiceProvider.GetRequiredService<IAuditService>()
            .RecordAsync(new AuditEntry(AuditActions.Login, AuditEntityTypes.Account, student.Id, student.Id));

        var entries = await new AuditLogReader(Context).GetEntriesAsync(new AuditLogQuery(User: "new.student"));

        entries.Items.Should().ContainSingle().Which.UserEmail.Should().Be("new.student@uni.edu");
    }
}
