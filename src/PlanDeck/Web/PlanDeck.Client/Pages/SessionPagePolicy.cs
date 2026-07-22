using Grpc.Core;
using PlanDeck.Core.Shared.Contracts;
using PlanDeck.Core.Shared.Validation;

namespace PlanDeck.Client.Pages;

public static class SessionPagePolicy
{
    public static bool CanManageSessions(ProjectRoleDto role) =>
        role is ProjectRoleDto.Admin or ProjectRoleDto.Owner;

    public static bool IsValidProjectId(Guid projectId) => projectId != Guid.Empty;

    public static bool IsProjectUnavailable(StatusCode statusCode) =>
        statusCode is StatusCode.NotFound or StatusCode.PermissionDenied;

    public static string MapErrorToResourceKey(StatusCode statusCode, string detail) => statusCode switch
    {
        StatusCode.InvalidArgument => MapInvalidArgument(detail),
        StatusCode.AlreadyExists => "Sessions_MemberDuplicate",
        StatusCode.FailedPrecondition => "Sessions_ActiveLocked",
        StatusCode.PermissionDenied => "Sessions_PermissionDenied",
        StatusCode.NotFound => "Projects_NotFound",
        _ => "Error_Generic"
    };

    private static string MapInvalidArgument(string detail) => detail switch
    {
        SessionValidationMessages.NameRequired => "Sessions_NameRequired",
        SessionValidationMessages.CustomScaleRequired => "Sessions_CustomScaleRequired",
        SessionValidationMessages.TaskTitleRequired => "Sessions_TaskTitleRequired",
        SessionMemberValidationMessages.EmailRequired => "Sessions_MemberInvalidEmail",
        _ => "Error_Generic"
    };
}
