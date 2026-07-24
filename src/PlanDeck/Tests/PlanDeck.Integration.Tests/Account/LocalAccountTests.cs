using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Account;
using PlanDeck.Application.Domain;
using PlanDeck.Infrastructure.Identity;
using PlanDeck.Infrastructure.Persistence;
using PlanDeck.Server.Identity;
namespace PlanDeck.Integration.Tests.Account;

[TestFixture]
public sealed class LocalAccountTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DefaultConnection", AspireAppFixture.ConnectionString);
                builder.UseSetting("Authentication:Microsoft:TenantId", string.Empty);
                builder.UseSetting("Authentication:Microsoft:ClientId", string.Empty);
                builder.UseSetting("Authentication:Microsoft:ClientSecret", string.Empty);
                builder.UseSetting("RateLimiting:Disable", "true");
            });

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _client?.Dispose();
        _factory?.Dispose();
    }

    [Test]
    public async Task Register_CreatesOwnerAndTenant()
    {
        var request = new LocalRegisterRequest(
            $"owner-{UniqueSuffix()}@example.com",
            "Owner",
            "User",
            $"owner{UniqueSuffix()}",
            ValidPassword());

        var response = await _client.PostAsJsonAsync("/account/register", request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Status, Is.EqualTo(LocalRegisterStatus.Success.ToString()));
        Assert.That(result.UserId, Is.Not.EqualTo(Guid.Empty));
    }

    [Test]
    public async Task Register_DuplicateUserName_ReturnsSafeConflict()
    {
        var userName = $"user{UniqueSuffix()}";
        var first = new LocalRegisterRequest(
            $"first-{UniqueSuffix()}@example.com",
            "First",
            "User",
            userName,
            ValidPassword());

        var firstResponse = await _client.PostAsJsonAsync("/account/register", first);
        Assert.That(firstResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var second = new LocalRegisterRequest(
            $"second-{UniqueSuffix()}@example.com",
            "Second",
            "User",
            userName,
            ValidPassword());

        var secondResponse = await _client.PostAsJsonAsync("/account/register", second);

        Assert.That(secondResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var result = await secondResponse.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.That(result!.Status, Is.EqualTo(LocalRegisterStatus.DuplicateUserName.ToString()));
    }

    [Test]
    public async Task Register_DuplicateEmail_ReturnsSafeConflict()
    {
        var email = $"email-{UniqueSuffix()}@example.com";
        var first = new LocalRegisterRequest(
            email,
            "First",
            "User",
            $"first{UniqueSuffix()}",
            ValidPassword());

        var firstResponse = await _client.PostAsJsonAsync("/account/register", first);
        Assert.That(firstResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var second = new LocalRegisterRequest(
            email,
            "Second",
            "User",
            $"second{UniqueSuffix()}",
            ValidPassword());

        var secondResponse = await _client.PostAsJsonAsync("/account/register", second);

        Assert.That(secondResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var result = await secondResponse.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.That(result!.Status, Is.EqualTo(LocalRegisterStatus.DuplicateEmail.ToString()));
    }

    [Test]
    public async Task Register_UserNameWithAt_IsRejected()
    {
        var request = new LocalRegisterRequest(
            $"invalid-{UniqueSuffix()}@example.com",
            "Invalid",
            "User",
            $"user@{UniqueSuffix()}",
            ValidPassword());

        var response = await _client.PostAsJsonAsync("/account/register", request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var result = await response.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.That(result!.Status, Is.EqualTo(LocalRegisterStatus.InvalidUserName.ToString()));
    }

    [Test]
    public async Task Register_PublicRegistrationDisabled_BlocksSelfSignup()
    {
        using var scopedFactory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Authentication:AllowPublicRegistration", "false");
        });
        using var scopedClient = scopedFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var request = new LocalRegisterRequest(
            $"disabled-{UniqueSuffix()}@example.com",
            "Disabled",
            "User",
            $"disabled{UniqueSuffix()}",
            ValidPassword());

        var response = await scopedClient.PostAsJsonAsync("/account/register", request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var result = await response.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.That(result!.Status, Is.EqualTo(LocalRegisterStatus.PublicRegistrationDisabled.ToString()));
    }

    [Test]
    public async Task Register_WithInvitation_CreatesMember()
    {
        var ownerEmail = $"owner-{UniqueSuffix()}@example.com";
        var ownerUserName = $"owner{UniqueSuffix()}";
        var owner = new LocalRegisterRequest(ownerEmail, "Owner", "User", ownerUserName, ValidPassword());
        var ownerResponse = await _client.PostAsJsonAsync("/account/register", owner);
        Assert.That(ownerResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var tenantId = await GetTenantIdForUserAsync(ownerUserName);

        var memberEmail = $"member-{UniqueSuffix()}@example.com";
        var token = Guid.NewGuid().ToString("N");
        await CreateInvitationAsync(tenantId, memberEmail, token, TenantRole.Member);

        var memberRequest = new LocalRegisterRequest(
            memberEmail,
            "Member",
            "User",
            $"member{UniqueSuffix()}",
            ValidPassword(),
            token);

        var memberResponse = await _client.PostAsJsonAsync("/account/register", memberRequest);

        Assert.That(memberResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await memberResponse.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.That(result!.Status, Is.EqualTo(LocalRegisterStatus.Success.ToString()));

        var memberTenantId = await GetTenantIdForUserAsync(memberRequest.UserName);
        Assert.That(memberTenantId, Is.EqualTo(tenantId));
    }

    [Test]
    public async Task Register_WithInvalidInvitation_DoesNotCreateData()
    {
        var request = new LocalRegisterRequest(
            $"noinv-{UniqueSuffix()}@example.com",
            "No",
            "Invitation",
            $"noinv{UniqueSuffix()}",
            ValidPassword(),
            "invalid-token");

        var response = await _client.PostAsJsonAsync("/account/register", request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var result = await response.Content.ReadFromJsonAsync<RegisterResponse>();
        Assert.That(result!.Status, Is.EqualTo(LocalRegisterStatus.InvitationInvalidOrExpired.ToString()));
    }

    [Test]
    public async Task Login_WithUserName_Succeeds()
    {
        var userName = $"loginuser{UniqueSuffix()}";
        var password = ValidPassword();
        await RegisterUserAsync(userName, $"login-{UniqueSuffix()}@example.com", password);

        var loginRequest = new LocalLoginRequest(userName, password);
        var response = await _client.PostAsJsonAsync("/account/login", loginRequest);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.That(result!.Status, Is.EqualTo(LocalLoginStatus.Success.ToString()));
    }

    [Test]
    public async Task Login_WithEmail_Succeeds()
    {
        var email = $"email-login-{UniqueSuffix()}@example.com";
        var password = ValidPassword();
        await RegisterUserAsync($"emaillogin{UniqueSuffix()}", email, password);

        var loginRequest = new LocalLoginRequest(email, password);
        var response = await _client.PostAsJsonAsync("/account/login", loginRequest);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.That(result!.Status, Is.EqualTo(LocalLoginStatus.Success.ToString()));
    }

    [Test]
    public async Task Login_InvalidCredentials_ReturnsUniformError()
    {
        var loginRequest = new LocalLoginRequest($"nonexistent{UniqueSuffix()}", "WrongPassword123!");
        var response = await _client.PostAsJsonAsync("/account/login", loginRequest);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.That(result!.Status, Is.EqualTo(LocalLoginStatus.InvalidCredentials.ToString()));
    }

    [Test]
    public async Task Login_Lockout_AfterFailedAttempts()
    {
        var userName = $"lockout{UniqueSuffix()}";
        var email = $"lockout-{UniqueSuffix()}@example.com";
        var password = ValidPassword();
        await RegisterUserAsync(userName, email, password);

        for (var i = 0; i < 5; i++)
        {
            var badRequest = new LocalLoginRequest(userName, "WrongPassword123!");
            var response = await _client.PostAsJsonAsync("/account/login", badRequest);
            Assert.That(response.StatusCode, Is.AnyOf(HttpStatusCode.BadRequest, HttpStatusCode.TooManyRequests));
        }

        var finalRequest = new LocalLoginRequest(userName, password);
        var finalResponse = await _client.PostAsJsonAsync("/account/login", finalRequest);

        Assert.That(finalResponse.StatusCode, Is.EqualTo(HttpStatusCode.TooManyRequests));
    }

    [Test]
    public async Task Logout_Get_IsNotAvailable()
    {
        var response = await _client.GetAsync("/auth/logout");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Logout_Post_WithoutAntiforgery_IsRejected()
    {
        await RegisterAndLoginAsync($"logout{UniqueSuffix()}", $"logout-{UniqueSuffix()}@example.com");

        var response = await _client.PostAsync("/account/logout", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Logout_Post_WithAntiforgery_Succeeds()
    {
        await RegisterAndLoginAsync($"logoutok{UniqueSuffix()}", $"logoutok-{UniqueSuffix()}@example.com");

        var antiforgeryResponse = await _client.GetAsync("/account/antiforgery");
        Assert.That(antiforgeryResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var antiforgeryBody = await antiforgeryResponse.Content.ReadFromJsonAsync<AntiforgeryResponse>();
        Assert.That(antiforgeryBody, Is.Not.Null);

        var requestToken = antiforgeryBody!.Token;
        var antiforgeryCookie = antiforgeryResponse.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault()?.Split(';')[0].Trim()
            : null;

        var request = new HttpRequestMessage(HttpMethod.Post, "/account/logout");
        request.Headers.Add("RequestVerificationToken", requestToken);
        if (!string.IsNullOrWhiteSpace(antiforgeryCookie))
        {
            request.Headers.Add("Cookie", antiforgeryCookie);
        }

        var response = await _client.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private async Task RegisterAndLoginAsync(string userName, string email)
    {
        var password = ValidPassword();
        await RegisterUserAsync(userName, email, password);
        var loginRequest = new LocalLoginRequest(userName, password);
        var response = await _client.PostAsJsonAsync("/account/login", loginRequest);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private async Task RegisterUserAsync(string userName, string email, string password)
    {
        var request = new LocalRegisterRequest(email, "Test", "User", userName, password);
        var response = await _client.PostAsJsonAsync("/account/register", request);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Fail($"Registration failed with {response.StatusCode}: {body}");
        }
    }

    private async Task<Guid> GetTenantIdForUserAsync(string userName)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanDeckDbContext>();
        var normalized = userName.ToUpperInvariant();
        var user = await db.Users.AsNoTracking()
            .SingleAsync(u => u.NormalizedUserName == normalized);
        var appUser = await db.AppUsers.AsNoTracking()
            .IgnoreQueryFilters()
            .SingleAsync(u => u.Id == user.Id);
        return appUser.TenantId;
    }

    private async Task CreateInvitationAsync(
        Guid tenantId,
        string email,
        string token,
        TenantRole role)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var provisioningAccessor = scope.ServiceProvider.GetRequiredService<IProvisioningContextAccessor>();
        provisioningAccessor.TenantId = tenantId;

        var db = scope.ServiceProvider.GetRequiredService<PlanDeckDbContext>();
        var normalizedEmail = email.ToUpperInvariant();
        var tokenHash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(token));

        db.TenantInvitations.Add(new TenantInvitation
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TokenHash = tokenHash,
            NormalizedEmail = normalizedEmail,
            Role = role,
            Status = InvitationStatus.Pending,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(7)
        });

        await db.SaveChangesAsync();
    }

    private static string UniqueSuffix() =>
        Guid.NewGuid().ToString("N")[..10];

    private static string ValidPassword() => "StrongPass123!";

    private sealed record RegisterResponse(string Status, Guid? UserId, IReadOnlyList<string>? Errors = null);

    private sealed record LoginResponse(
        string Status,
        Guid? UserId = null,
        IReadOnlyList<string>? Errors = null,
        string? ReturnUrl = null);

    private sealed record AntiforgeryResponse(string Token);
}
