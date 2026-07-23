using Grpc.Core;
using PlanDeck.Client.Pages;
using PlanDeck.Core.Shared.Contracts;
using PlanDeck.Core.Shared.Validation;

namespace PlanDeck.Unit.Tests.Client;

[TestFixture]
public class SessionRoleUiTests
{
    [TestCase(ProjectRoleDto.Owner, true)]
    [TestCase(ProjectRoleDto.Admin, true)]
    [TestCase(ProjectRoleDto.Member, false)]
    public void CanManageSessions_ReturnsExpectedValue(ProjectRoleDto role, bool expected)
    {
        var result = SessionPagePolicy.CanManageSessions(role);
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void IsValidProjectId_EmptyGuid_ReturnsFalse()
    {
        var result = SessionPagePolicy.IsValidProjectId(Guid.Empty);
        Assert.That(result, Is.False);
    }

    [Test]
    public void IsValidProjectId_NonEmptyGuid_ReturnsTrue()
    {
        var result = SessionPagePolicy.IsValidProjectId(Guid.NewGuid());
        Assert.That(result, Is.True);
    }

    [TestCase(StatusCode.NotFound, true)]
    [TestCase(StatusCode.PermissionDenied, true)]
    [TestCase(StatusCode.InvalidArgument, false)]
    public void IsProjectUnavailable_MapsExpectedCodes(StatusCode statusCode, bool expected)
    {
        var result = SessionPagePolicy.IsProjectUnavailable(statusCode);
        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase(StatusCode.PermissionDenied, "", "Sessions_PermissionDenied")]
    [TestCase(StatusCode.NotFound, "", "Projects_NotFound")]
    [TestCase(StatusCode.AlreadyExists, "", "Sessions_MemberDuplicate")]
    [TestCase(StatusCode.FailedPrecondition, "", "Sessions_ActiveLocked")]
    [TestCase(StatusCode.InvalidArgument, SessionValidationMessages.NameRequired, "Sessions_NameRequired")]
    [TestCase(StatusCode.InvalidArgument, SessionValidationMessages.CustomScaleRequired, "Sessions_CustomScaleRequired")]
    [TestCase(StatusCode.InvalidArgument, SessionValidationMessages.TaskTitleRequired, "Sessions_TaskTitleRequired")]
    [TestCase(StatusCode.InvalidArgument, SessionMemberValidationMessages.EmailRequired, "Sessions_MemberInvalidEmail")]
    public void MapErrorToResourceKey_ReturnsExpectedKey(StatusCode statusCode, string detail, string expectedKey)
    {
        var key = SessionPagePolicy.MapErrorToResourceKey(statusCode, detail);
        Assert.That(key, Is.EqualTo(expectedKey));
    }
}
