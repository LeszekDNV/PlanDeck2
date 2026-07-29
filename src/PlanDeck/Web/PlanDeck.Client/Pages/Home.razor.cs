using Grpc.Core;

namespace PlanDeck.Client.Pages;

public partial class Home
{
    private HomePageView _view = HomePageView.Loading;
    private bool _microsoftAuthenticationAvailable;
    private string? _sessionCode;

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        var view = HomePagePolicy.GetView(state.User);

        if (view == HomePageView.Anonymous)
        {
            try
            {
                _microsoftAuthenticationAvailable =
                    await AccountService.IsMicrosoftAuthenticationAvailableAsync();
            }
            catch (RpcException exception)
            {
                Logger.LogWarning(
                    exception,
                    "Microsoft authentication capability check failed on the Home page.");
            }
        }

        _view = view;
    }

    private void Register() => Navigation.NavigateTo("/account/register");

    private void Login() => Navigation.NavigateTo("/account/login?returnUrl=%2F");

    private void LoginWithMicrosoft() => AccountService.NavigateToEntraLogin("/");

    private void OpenProjects() => Navigation.NavigateTo("/projects");

    private void CreateProject() => Navigation.NavigateTo("/projects?create=true");

    private void OpenTeams() => Navigation.NavigateTo("/teams");

    private void JoinSession()
    {
        var route = HomePagePolicy.BuildJoinRoute(_sessionCode);
        if (route is not null)
        {
            Navigation.NavigateTo(route);
        }
    }
}
