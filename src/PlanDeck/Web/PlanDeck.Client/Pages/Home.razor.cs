namespace PlanDeck.Client.Pages;

public partial class Home
{
    private string? _serverResponse;

    private async Task CallServerAsync()
    {
        _serverResponse = await HelloService.GetHelloAsync();
    }
}
