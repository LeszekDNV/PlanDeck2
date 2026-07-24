namespace PlanDeck.Infrastructure.Identity;

public sealed class EmailSettings
{
    public const string SectionName = "EmailSettings";

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 25;

    public bool UseTls { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string SenderAddress { get; set; } = string.Empty;

    public string SenderName { get; set; } = "PlanDeck";

    public string PublicBaseUrl { get; set; } = string.Empty;

    public int RetryCount { get; set; } = 3;

    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
}
