namespace PlanDeck.Server.Testing;

public sealed record TestMemberIdentity(
    Guid AppUserId,
    Guid EntraObjectId,
    string SelectionKey,
    string DisplayName,
    string Email);

public static class TestMemberIdentities
{
    public static readonly Guid TenantId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static readonly TestMemberIdentity Owner = new(
        Guid.Parse("aaaaaaaa-2222-2222-2222-222222222222"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "owner",
        "Test Owner",
        "test.owner@plandeck.local");

    public static readonly TestMemberIdentity Admin = new(
        Guid.Parse("bbbbbbbb-3333-3333-3333-333333333333"),
        Guid.Parse("33333333-3333-3333-3333-333333333333"),
        "admin",
        "Test Admin",
        "test.admin@plandeck.local");

    public static readonly TestMemberIdentity Member = new(
        Guid.Parse("cccccccc-4444-4444-4444-444444444444"),
        Guid.Parse("55555555-5555-5555-5555-555555555555"),
        "member",
        "Test Member",
        "test.member@plandeck.local");

    public static IReadOnlyList<TestMemberIdentity> All { get; } = [Owner, Admin, Member];
}
