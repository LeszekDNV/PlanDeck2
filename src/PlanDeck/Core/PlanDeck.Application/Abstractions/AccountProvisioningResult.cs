namespace PlanDeck.Application.Abstractions;

public sealed record AccountProvisioningResult(
    Guid ApplicationUserId,
    Guid TenantId,
    Guid AppUserId,
    bool Succeeded,
    IReadOnlyList<string> Errors)
{
    public static AccountProvisioningResult Failure(IReadOnlyList<string> errors) =>
        new(Guid.Empty, Guid.Empty, Guid.Empty, false, errors);
}

