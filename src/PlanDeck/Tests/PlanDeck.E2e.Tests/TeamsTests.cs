using Microsoft.Playwright;
using PlanDeck.E2e.Tests.Pages;

namespace PlanDeck.E2e.Tests;

[TestFixture]
public class TeamsTests : PageTest
{
    public override BrowserNewContextOptions ContextOptions() => new()
    {
        IgnoreHTTPSErrors = true
    };

    [Test]
    public async Task CreateTeam_AddMember_RendersBoth()
    {
        var teamName = $"E2E Team {Guid.NewGuid():N}";
        var memberEmail = $"e2e-{Guid.NewGuid():N}@example.com";

        var teams = new TeamsPage(Page, AspireAppFixture.BaseUrl);

        await teams.GotoAsync();
        await teams.CreateTeamAsync(teamName);
        await teams.AddMemberAsync(memberEmail);

        await Expect(teams.MemberEntry(memberEmail)).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Test]
    public async Task RemoveMember_AfterConfirmation_RemovesFromList()
    {
        var teamName = $"E2E Team {Guid.NewGuid():N}";
        var memberEmail = $"e2e-{Guid.NewGuid():N}@example.com";

        var teams = new TeamsPage(Page, AspireAppFixture.BaseUrl);

        await teams.GotoAsync();
        await teams.CreateTeamAsync(teamName);
        await teams.AddMemberAsync(memberEmail);
        await Expect(teams.MemberEntry(memberEmail)).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await teams.RemoveMemberAsync(memberEmail);
        await Expect(teams.MemberEntry(memberEmail)).Not.ToBeVisibleAsync(new() { Timeout = 15_000 });
    }
}
