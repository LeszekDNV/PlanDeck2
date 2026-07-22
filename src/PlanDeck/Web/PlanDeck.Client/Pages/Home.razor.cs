namespace PlanDeck.Client.Pages;

public partial class Home
{
    private string? _serverResponse;

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        if (state.User.Identity?.IsAuthenticated == true && !state.User.HasClaim("is_guest", "true"))
        {
            Navigation.NavigateTo("/projects", replace: true);
        }
    }

    private async Task CallServerAsync()
    {
        _serverResponse = await HelloService.GetHelloAsync();
    }
}
