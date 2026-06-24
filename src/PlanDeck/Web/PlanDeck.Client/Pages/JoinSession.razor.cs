using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;

namespace PlanDeck.Client.Pages;

public partial class JoinSession
{
    [Parameter]
    public string Code { get; set; } = string.Empty;

    private string _displayName = string.Empty;
    private bool _busy;
    private string? _errorKey;

    private async Task JoinAsync()
    {
        var name = _displayName.Trim();
        if (name.Length == 0)
        {
            _errorKey = "Join_NameRequired";
            return;
        }

        _busy = true;
        _errorKey = null;
        try
        {
            var response = await Http.PostAsJsonAsync(
                "guest/join",
                new { code = Code, displayName = name });

            if (response.IsSuccessStatusCode)
            {
                var payload = await response.Content.ReadFromJsonAsync<GuestJoinResult>();
                if (payload is null || payload.SessionId == Guid.Empty)
                {
                    _errorKey = "Join_Error";
                    return;
                }

                // Full reload so the freshly issued guest cookie is sent and the auth state refreshes.
                Navigation.NavigateTo($"/voting/{payload.SessionId}", forceLoad: true);
                return;
            }

            _errorKey = response.StatusCode switch
            {
                HttpStatusCode.NotFound => "Join_UnknownCode",
                HttpStatusCode.Conflict => "Join_Inactive",
                HttpStatusCode.BadRequest => "Join_NameRequired",
                _ => "Join_Error"
            };
        }
        catch (Exception)
        {
            _errorKey = "Join_Error";
        }
        finally
        {
            _busy = false;
        }
    }

    private sealed record GuestJoinResult(Guid SessionId);

    private void LoginWithMicrosoft() =>
        Navigation.NavigateTo("/auth/login?returnUrl=/sessions", forceLoad: true);
}
