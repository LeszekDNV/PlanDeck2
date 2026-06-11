namespace PlanDeck.Client.Services;

public interface IHelloClientService
{
    Task<string> GetHelloAsync();
}
