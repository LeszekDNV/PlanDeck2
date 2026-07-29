namespace PlanDeck.Server.Identity;

public sealed class MicrosoftAuthenticationOptions
{
    public const string SectionName = "Authentication:Microsoft";

    public string? TenantId { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public string CallbackPath { get; set; } = "/signin-oidc";

    public bool Required { get; set; }

    public bool IsAvailable =>
        !string.IsNullOrWhiteSpace(TenantId)
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret);

    public void Validate()
    {
        if (Required && !IsAvailable)
        {
            throw new InvalidOperationException(
                "Microsoft authentication is required. Configure "
                + "Authentication:Microsoft:TenantId, ClientId, and ClientSecret.");
        }
    }
}
