namespace PlanDeck.Server.Testing;

public sealed record TestMemberIdentity(
    Guid AppUserId,
    Guid EntraObjectId,
    string DisplayName,
    string Email);

public static class TestMemberIdentities
{
    public static readonly Guid TenantId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static readonly TestMemberIdentity Default = new(
        Guid.Parse("aaaaaaaa-2222-2222-2222-222222222222"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "Test User",
        "test.user@plandeck.local");

    public static readonly TestMemberIdentity Second = new(
        Guid.Parse("bbbbbbbb-3333-3333-3333-333333333333"),
        Guid.Parse("33333333-3333-3333-3333-333333333333"),
        "Test User B",
        "test.userb@plandeck.local");

    public static IReadOnlyList<TestMemberIdentity> All { get; } = [Default, Second];
}
