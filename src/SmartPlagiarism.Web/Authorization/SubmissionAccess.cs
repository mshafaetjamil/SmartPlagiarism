using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.Identity;

namespace SmartPlagiarism.Web.Authorization;

/// <summary>The submission an authorization check is about.</summary>
public sealed record SubmissionResource(int SubmissionId);

/// <summary>Satisfied when the user may see the submission.</summary>
public sealed class SubmissionAccessRequirement : IAuthorizationRequirement;

/// <summary>
/// Resource-based authorization for <see cref="AuthorizationPolicies.CanViewSubmission"/>.
///
/// Delegates to <see cref="ISubmissionAccessService"/> so the rule lives in one
/// place; this handler only turns the signed-in principal into a viewer. Roles
/// alone cannot express "only your own", which is why this is a handler rather
/// than another role policy.
/// </summary>
public sealed class SubmissionAccessHandler
    : AuthorizationHandler<SubmissionAccessRequirement, SubmissionResource>
{
    private readonly ISubmissionAccessService _access;

    public SubmissionAccessHandler(ISubmissionAccessService access)
    {
        _access = access;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SubmissionAccessRequirement requirement,
        SubmissionResource resource)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrEmpty(userId))
        {
            return;
        }

        var viewer = new SubmissionViewer(userId, IsStaff(context.User), IsAdmin: context.User.IsInRole(ApplicationRoles.Admin));

        if (await _access.CanViewAsync(resource.SubmissionId, viewer))
        {
            context.Succeed(requirement);
        }
    }

    public static bool IsStaff(ClaimsPrincipal user) =>
        user.IsInRole(ApplicationRoles.Teacher) || user.IsInRole(ApplicationRoles.Admin);
}
