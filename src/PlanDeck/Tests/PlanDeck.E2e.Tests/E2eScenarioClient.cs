using System.Net.Http.Json;

namespace PlanDeck.E2e.Tests;

public sealed class E2eScenarioClient(HttpClient httpClient)
{
    private const string ScenarioTokenHeader = "X-PlanDeck-Test-Token";

    public static E2eScenarioClient Create(string baseUrl, string token)
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(baseUrl, UriKind.Absolute)
        };
        client.DefaultRequestHeaders.Add(ScenarioTokenHeader, token);
        return new E2eScenarioClient(client);
    }

    public async Task<E2eScenarioSeedResponse> SeedAsync(
        Guid runId,
        E2eScenarioSessionStatus sessionStatus = E2eScenarioSessionStatus.Draft,
        int taskCount = 1,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            "/testing/e2e-scenarios/",
            new E2eScenarioSeedRequest(runId, sessionStatus, taskCount),
            cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<E2eScenarioSeedResponse>(cancellationToken))
            ?? throw new InvalidOperationException("Scenario seed response was empty.");
    }

    public async Task<E2eScenarioCleanupResponse> CleanupAsync(
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.DeleteAsync(
            $"/testing/e2e-scenarios/{runId:D}",
            cancellationToken);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<E2eScenarioCleanupResponse>(cancellationToken))
            ?? throw new InvalidOperationException("Scenario cleanup response was empty.");
    }
}

public enum E2eScenarioSessionStatus
{
    Draft = 0,
    Active = 1
}

public sealed record E2eScenarioSeedRequest(
    Guid RunId,
    E2eScenarioSessionStatus SessionStatus = E2eScenarioSessionStatus.Draft,
    int TaskCount = 1);

public sealed record E2eScenarioSeedResponse(
    Guid RunId,
    Guid ProjectId,
    Guid SessionId,
    Guid OwnerUserId,
    Guid AdminUserId,
    Guid MemberUserId);

public sealed record E2eScenarioCleanupResponse(
    Guid RunId,
    int DeletedProjectCount);
