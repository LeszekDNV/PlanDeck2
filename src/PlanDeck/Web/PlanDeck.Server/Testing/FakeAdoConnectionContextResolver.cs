using PlanDeck.Application.Abstractions;

namespace PlanDeck.Server.Testing;

/// <summary>
/// Deterministic stub <see cref="IAdoConnectionContextResolver"/> for the test scheme.
/// Returns a fixed context for any project, so tests don't need a real Key Vault or ADO connection.
/// </summary>
public sealed class FakeAdoConnectionContextResolver : IAdoConnectionContextResolver
{
    public Task<AdoConnectionContext> ResolveAsync(Guid projectId, CancellationToken cancellationToken)
        => Task.FromResult(new AdoConnectionContext(
            "https://dev.azure.com/test",
            "TestProject",
            "fake-pat",
            "Story Points",
            "Description",
            "Repro Steps",
            "Acceptance Criteria"));
}
