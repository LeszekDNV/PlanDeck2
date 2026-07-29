using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using MudBlazor;
using PlanDeck.Client.Services;

namespace PlanDeck.Client.Layout;

public partial class MainLayout
{
    [Inject]
    private IAccountClientService AccountService { get; set; } = null!;

    private bool _drawerOpen;
    private bool _isDarkMode = true;
    private MudTheme? _theme;

    private void Login() =>
        Navigation.NavigateTo(
            $"/account/login?returnUrl={Uri.EscapeDataString(Navigation.Uri)}",
            forceLoad: true);

    private async Task LogoutAsync(ClaimsPrincipal user)
    {
        if (IsGuest(user))
        {
            await AccountService.LogoutGuestAsync();
            return;
        }

        await AccountService.LogoutAsync();
    }

    private async Task LogoutAndCloseAsync(ClaimsPrincipal user)
    {
        _drawerOpen = false;
        await LogoutAsync(user);
    }

    private static bool IsGuest(ClaimsPrincipal user) =>
        string.Equals(
            user.FindFirst("is_guest")?.Value,
            bool.TrueString,
            StringComparison.OrdinalIgnoreCase);

    private async Task SetCultureAsync(string culture)
    {
        await JS.InvokeVoidAsync("localStorage.setItem", "BlazorCulture", culture);
        Navigation.NavigateTo(Navigation.Uri, forceLoad: true);
    }

    private void NavigateToAndClose(string path)
    {
        _drawerOpen = false;
        Navigation.NavigateTo(path);
    }

    private async Task SetCultureAndCloseAsync(string culture)
    {
        _drawerOpen = false;
        await SetCultureAsync(culture);
    }

    private void LoginAndClose()
    {
        _drawerOpen = false;
        Login();
    }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();

        _theme = new()
        {
            PaletteLight = _lightPalette,
            PaletteDark = _darkPalette,
            LayoutProperties = new LayoutProperties()
        };

        var theme = await JS.InvokeAsync<string>("getPlanDeckThemePreference");
        _isDarkMode = theme != "light";
    }

    private void DrawerToggle()
    {
        _drawerOpen = !_drawerOpen;
    }

    private async Task DarkModeToggleAsync()
    {
        _isDarkMode = !_isDarkMode;
        var theme = _isDarkMode ? "dark" : "light";
        await JS.InvokeVoidAsync("setPlanDeckThemePreference", theme);
    }

    private readonly PaletteLight _lightPalette = new()
    {
        Black = "#110e2d",
        AppbarText = "#424242",
        AppbarBackground = "rgba(255,255,255,0.8)",
        DrawerBackground = "#ffffff",
        GrayLight = "#e8e8e8",
        GrayLighter = "#f9f9f9",
    };

    private readonly PaletteDark _darkPalette = new()
    {
        Primary = "#7e6fff",
        Surface = "#1e1e2d",
        Background = "#1a1a27",
        BackgroundGray = "#151521",
        AppbarText = "#92929f",
        AppbarBackground = "rgba(26,26,39,0.8)",
        DrawerBackground = "#1a1a27",
        ActionDefault = "#74718e",
        ActionDisabled = "#9999994d",
        ActionDisabledBackground = "#605f6d4d",
        TextPrimary = "#b2b0bf",
        TextSecondary = "#92929f",
        TextDisabled = "#ffffff33",
        DrawerIcon = "#92929f",
        DrawerText = "#92929f",
        GrayLight = "#2a2833",
        GrayLighter = "#1e1e2d",
        Info = "#4a86ff",
        Success = "#3dcb6c",
        Warning = "#ffb545",
        Error = "#ff3f5f",
        LinesDefault = "#33323e",
        TableLines = "#33323e",
        Divider = "#292838",
        OverlayLight = "#1e1e2d80",
    };

    public string DarkLightModeButtonIcon => _isDarkMode switch
    {
        true => Icons.Material.Rounded.LightMode,
        false => Icons.Material.Rounded.DarkMode,
    };
}
