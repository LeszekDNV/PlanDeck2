using System.Net.Http.Json;
using System.Text.Json;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Components;
using PlanDeck.Client.Models;
using PlanDeck.Core.Shared.Contracts;
using ProtoBuf.Grpc.Client;

namespace PlanDeck.Client.Services;

public sealed class AccountClientService(
    HttpClient httpClient,
    GrpcChannel grpcChannel,
    NavigationManager navigation) : IAccountClientService
{
    private const string AntiforgeryHeader = "RequestVerificationToken";
    private static readonly JsonSerializerOptions ResponseJsonOptions = new(JsonSerializerDefaults.Web);
    private Task<bool>? _microsoftAuthenticationAvailability;

    public async Task<AccountActionResponse> RegisterAsync(
        LocalRegisterModel model,
        CancellationToken cancellationToken = default)
    {
        await EnsureAntiforgeryTokenAsync(cancellationToken);
        var response = await httpClient.PostAsJsonAsync(
            "account/register",
            model,
            cancellationToken);

        return await ReadResponseAsync(response, cancellationToken);
    }

    public async Task<AccountActionResponse> LoginAsync(
        LocalLoginModel model,
        string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureAntiforgeryTokenAsync(cancellationToken);
        var url = string.IsNullOrWhiteSpace(returnUrl)
            ? "account/login"
            : $"account/login?returnUrl={Uri.EscapeDataString(returnUrl)}";

        var response = await httpClient.PostAsJsonAsync(
            url,
            model,
            cancellationToken);

        var result = await ReadResponseAsync(response, cancellationToken);
        if (result.ReturnUrl is not null)
        {
            navigation.NavigateTo(result.ReturnUrl, forceLoad: true);
        }

        return result;
    }

    public Task<AccountActionResponse> LogoutAsync(CancellationToken cancellationToken = default) =>
        LogoutCoreAsync("account/logout", cancellationToken);

    public Task<AccountActionResponse> LogoutGuestAsync(CancellationToken cancellationToken = default) =>
        LogoutCoreAsync("guest/logout", cancellationToken);

    private async Task<AccountActionResponse> LogoutCoreAsync(
        string endpoint,
        CancellationToken cancellationToken)
    {
        await EnsureAntiforgeryTokenAsync(cancellationToken);
        var response = await httpClient.PostAsync(endpoint, null, cancellationToken);
        var result = await ReadResponseAsync(response, cancellationToken);

        if (result.ReturnUrl is not null)
        {
            navigation.NavigateTo(result.ReturnUrl, forceLoad: true);
        }

        return result;
    }

    public async Task<AccountActionResponse> ConfirmEmailAsync(
        Guid userId,
        string token,
        CancellationToken cancellationToken = default)
    {
        var url = $"api/account/confirm-email?userId={userId}&token={Uri.EscapeDataString(token)}";
        var response = await httpClient.GetAsync(url, cancellationToken);
        return await ReadResponseAsync(response, cancellationToken);
    }

    public async Task<AccountActionResponse> ResendConfirmationAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        await EnsureAntiforgeryTokenAsync(cancellationToken);
        var response = await httpClient.PostAsJsonAsync(
            "account/resend-confirmation",
            new ResendConfirmationModel(email),
            cancellationToken);

        return await ReadResponseAsync(response, cancellationToken);
    }

    public async Task<AccountActionResponse> ForgotPasswordAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        await EnsureAntiforgeryTokenAsync(cancellationToken);
        var response = await httpClient.PostAsJsonAsync(
            "account/forgot-password",
            new ForgotPasswordModel(email),
            cancellationToken);

        return await ReadResponseAsync(response, cancellationToken);
    }

    public async Task<AccountActionResponse> ResetPasswordAsync(
        ResetPasswordModel model,
        CancellationToken cancellationToken = default)
    {
        await EnsureAntiforgeryTokenAsync(cancellationToken);
        var response = await httpClient.PostAsJsonAsync(
            "account/reset-password",
            model,
            cancellationToken);

        return await ReadResponseAsync(response, cancellationToken);
    }

    public async Task<bool> IsMicrosoftAuthenticationAvailableAsync(
        CancellationToken cancellationToken = default)
    {
        var request = _microsoftAuthenticationAvailability ??=
            LoadMicrosoftAuthenticationAvailabilityAsync(cancellationToken);

        try
        {
            return await request;
        }
        catch (RpcException)
        {
            ResetMicrosoftAuthenticationAvailability(request);
            throw;
        }
        catch (OperationCanceledException)
        {
            ResetMicrosoftAuthenticationAvailability(request);
            throw;
        }
    }

    public void NavigateToEntraLogin(string? returnUrl = null)
    {
        var url = string.IsNullOrWhiteSpace(returnUrl)
            ? "account/entra/login"
            : $"account/entra/login?returnUrl={Uri.EscapeDataString(returnUrl)}";

        navigation.NavigateTo(url, forceLoad: true);
    }

    public void NavigateToEntraRegister(string? returnUrl = null, string? invitationToken = null)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(returnUrl))
        {
            query.Add($"returnUrl={Uri.EscapeDataString(returnUrl)}");
        }

        if (!string.IsNullOrWhiteSpace(invitationToken))
        {
            query.Add($"invitationToken={Uri.EscapeDataString(invitationToken)}");
        }

        var url = "account/entra/register";
        if (query.Count > 0)
        {
            url += $"?{string.Join("&", query)}";
        }

        navigation.NavigateTo(url, forceLoad: true);
    }

    public async Task<AccountActionResponse> LinkEntraAsync(
        LinkEntraModel model,
        CancellationToken cancellationToken = default)
    {
        await EnsureAntiforgeryTokenAsync(cancellationToken);
        var response = await httpClient.PostAsJsonAsync(
            "account/entra/link",
            model,
            cancellationToken);

        var result = await ReadResponseAsync(response, cancellationToken);
        if (result.ReturnUrl is not null)
        {
            navigation.NavigateTo(result.ReturnUrl, forceLoad: true);
        }

        return result;
    }

    public async Task<AccountActionResponse> UnlinkEntraAsync(
        UnlinkEntraModel model,
        CancellationToken cancellationToken = default)
    {
        await EnsureAntiforgeryTokenAsync(cancellationToken);
        var response = await httpClient.PostAsJsonAsync(
            "account/entra/unlink",
            model,
            cancellationToken);

        return await ReadResponseAsync(response, cancellationToken);
    }

    public async Task<SecurityInfoModel> GetSecurityInfoAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync("account/security-info", cancellationToken);
        response.EnsureSuccessStatusCode();

        var info = await response.Content.ReadFromJsonAsync<SecurityInfoModel>(cancellationToken);
        return info ?? throw new InvalidOperationException("Failed to read security info.");
    }

    private async Task<bool> LoadMicrosoftAuthenticationAvailabilityAsync(
        CancellationToken cancellationToken)
    {
        var service = grpcChannel.CreateGrpcService<IAuthService>();
        var reply = await service.GetAuthenticationCapabilitiesAsync(
            new AuthenticationCapabilitiesRequest(),
            cancellationToken);
        return reply.MicrosoftAuthenticationAvailable;
    }

    private void ResetMicrosoftAuthenticationAvailability(Task<bool> request)
    {
        if (ReferenceEquals(_microsoftAuthenticationAvailability, request))
        {
            _microsoftAuthenticationAvailability = null;
        }
    }

    private async Task EnsureAntiforgeryTokenAsync(CancellationToken cancellationToken)
    {
        if (httpClient.DefaultRequestHeaders.Contains(AntiforgeryHeader))
        {
            return;
        }

        var response = await httpClient.GetAsync("account/antiforgery", cancellationToken);
        response.EnsureSuccessStatusCode();

        var token = await response.Content.ReadFromJsonAsync<AntiforgeryTokenResponse>(
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(token?.Token))
        {
            httpClient.DefaultRequestHeaders.Remove(AntiforgeryHeader);
            httpClient.DefaultRequestHeaders.Add(AntiforgeryHeader, token.Token);
        }
    }

    private static async Task<AccountActionResponse> ReadResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(content))
        {
            return new AccountActionResponse(
                "Failure",
                null,
                [$"Request failed with status {(int)response.StatusCode}."]);
        }

        try
        {
            var result = JsonSerializer.Deserialize<AccountActionResponse>(content, ResponseJsonOptions);
            if (result is not null)
            {
                return result;
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body (e.g., HTML from middleware or 403 page).
        }

        return new AccountActionResponse(
            "Failure",
            null,
            [content.Length > 200 ? $"{content[..200]}..." : content]);
    }

    private sealed record AntiforgeryTokenResponse(string Token);
}
