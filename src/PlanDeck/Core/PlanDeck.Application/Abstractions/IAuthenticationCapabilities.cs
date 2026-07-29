namespace PlanDeck.Application.Abstractions;

public interface IAuthenticationCapabilities
{
    bool MicrosoftAuthenticationAvailable { get; }
}
