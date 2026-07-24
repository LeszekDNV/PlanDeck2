using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
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
    public async Task Login_UnconfirmedEmail_ReturnsUniformInvalidCredentials()
    {
        var userName = $"unconfirmed{UniqueSuffix()}";
        var email = $"unconfirmed-{UniqueSuffix()}@example.com";
        var password = ValidPassword();
        await RegisterUserAsync(userName, email, password, confirmEmail: false);

        var response = await _client.PostAsJsonAsync("/account/login", new LocalLoginRequest(userName, password));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.That(result!.Status, Is.EqualTo(LocalLoginStatus.InvalidCredentials.ToString()));
    }

    [Test]
    public async Task ConfirmEmail_WithValidToken_ConfirmsAndAcceptsInvitation()
    {
        var ownerEmail = $"owner-{UniqueSuffix()}@example.com";
        var ownerUserName = $"owner{UniqueSuffix()}";
        var ownerPassword = ValidPassword();
        await RegisterUserAsync(ownerUserName, ownerEmail, ownerPassword);

        var tenantId = await GetTenantIdForUserAsync(ownerUserName);
        var memberEmail = $"member-{UniqueSuffix()}@example.com";
        var token = Guid.NewGuid().ToString("N");
        await CreateInvitationAsync(tenantId, memberEmail, token, TenantRole.Member);

        var memberUserName = $"member{UniqueSuffix()}";
        var memberPassword = ValidPassword();
        var memberRequest = new LocalRegisterRequest(
            memberEmail,
            "Member",
            "User",
            memberUserName,
            memberPassword,
            token);

        var registerResponse = await _client.PostAsJsonAsync("/account/register", memberRequest);
        Assert.That(registerResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var registerResult = await registerResponse.Content.ReadFromJsonAsync<RegisterResponse>();
        var memberUserId = registerResult!.UserId!.Value;

        var confirmResponse = await ConfirmEmailDirectAsync(memberUserId);

        Assert.That(confirmResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var confirmResult = await confirmResponse.Content.ReadFromJsonAsync<AccountStatusResponse>();
        Assert.That(confirmResult!.Status, Is.EqualTo(ConfirmEmailStatus.Success.ToString()));

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanDeckDbContext>();
        var invitation = await db.TenantInvitations
            .IgnoreQueryFilters()
            .SingleAsync(i => i.TenantId == tenantId && i.NormalizedEmail == memberEmail.ToUpperInvariant());
        Assert.That(invitation.Status, Is.EqualTo(InvitationStatus.Accepted));
    }

    [Test]
    public async Task ConfirmEmail_AlreadyConfirmed_ReturnsAlreadyConfirmed()
    {
        var userId = await RegisterUserAsync($"already{UniqueSuffix()}", $"already-{UniqueSuffix()}@example.com", ValidPassword());

        var first = await ConfirmEmailDirectAsync(userId);
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var second = await ConfirmEmailDirectAsync(userId);
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await second.Content.ReadFromJsonAsync<AccountStatusResponse>();
        Assert.That(result!.Status, Is.EqualTo(ConfirmEmailStatus.AlreadyConfirmed.ToString()));
    }

    [Test]
    public async Task ConfirmEmail_InvalidToken_ReturnsInvalidToken()
    {
        var userId = await RegisterUserAsync($"badtoken{UniqueSuffix()}", $"badtoken-{UniqueSuffix()}@example.com", ValidPassword(), confirmEmail: false);

        var response = await _client.GetAsync($"/api/account/confirm-email?userId={userId}&token=invalid-token");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var result = await response.Content.ReadFromJsonAsync<AccountStatusResponse>();
        Assert.That(result!.Status, Is.EqualTo(ConfirmEmailStatus.InvalidToken.ToString()));
    }

    [Test]
    public async Task ResendConfirmation_ForUnconfirmedAccount_ReturnsPublicResult()
    {
        var email = $"resend-{UniqueSuffix()}@example.com";
        await RegisterUserAsync($"resend{UniqueSuffix()}", email, ValidPassword(), confirmEmail: false);

        var response = await _client.PostAsJsonAsync("/account/resend-confirmation", new ResendConfirmationRequest(email));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<AccountStatusResponse>();
        Assert.That(result!.Status, Is.AnyOf(
            ResendConfirmationStatus.Sent.ToString(),
            ResendConfirmationStatus.SendFailed.ToString()));
    }

    [Test]
    public async Task ForgotPassword_ForUnconfirmedAccount_ReturnsSentWithoutLeak()
    {
        var email = $"forgot-unconfirmed-{UniqueSuffix()}@example.com";
        await RegisterUserAsync($"forgotunconfirmed{UniqueSuffix()}", email, ValidPassword(), confirmEmail: false);

        var response = await _client.PostAsJsonAsync("/account/forgot-password", new ForgotPasswordRequest(email));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<AccountStatusResponse>();
        Assert.That(result!.Status, Is.EqualTo(ForgotPasswordStatus.Sent.ToString()));
    }

    [Test]
    public async Task ForgotPassword_ForUnknownEmail_ReturnsSentWithoutLeak()
    {
        var response = await _client.PostAsJsonAsync(
            "/account/forgot-password",
            new ForgotPasswordRequest($"unknown-{UniqueSuffix()}@example.com"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<AccountStatusResponse>();
        Assert.That(result!.Status, Is.EqualTo(ForgotPasswordStatus.Sent.ToString()));
    }

    [Test]
    public async Task ResetPassword_WithValidToken_ChangesPassword()
    {
        var userName = $"reset{UniqueSuffix()}";
        var email = $"reset-{UniqueSuffix()}@example.com";
        var oldPassword = ValidPassword();
        var userId = await RegisterUserAsync(userName, email, oldPassword);

        var token = await GeneratePasswordResetTokenAsync(userId);
        var newPassword = "NewStrongPass123!";

        var response = await _client.PostAsJsonAsync(
            "/account/reset-password",
            new ResetPasswordRequest(email, token, newPassword));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<AccountStatusResponse>();
        Assert.That(result!.Status, Is.EqualTo(ResetPasswordStatus.Success.ToString()));

        var oldLogin = await _client.PostAsJsonAsync("/account/login", new LocalLoginRequest(userName, oldPassword));
        Assert.That(oldLogin.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var newLogin = await _client.PostAsJsonAsync("/account/login", new LocalLoginRequest(userName, newPassword));
        Assert.That(newLogin.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task ResetPassword_WithInvalidToken_ReturnsInvalidToken()
    {
        var email = $"reset-bad-{UniqueSuffix()}@example.com";
        await RegisterUserAsync($"resetbad{UniqueSuffix()}", email, ValidPassword());

        var response = await _client.PostAsJsonAsync(
            "/account/reset-password",
            new ResetPasswordRequest(email, "invalid-token", "NewStrongPass123!"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var result = await response.Content.ReadFromJsonAsync<AccountStatusResponse>();
        Assert.That(result!.Status, Is.EqualTo(ResetPasswordStatus.InvalidToken.ToString()));
    }

    [Test]
    public async Task ResetPassword_InvalidatesExistingSession()
    {
        var userName = $"resetsession{UniqueSuffix()}";
        var email = $"resetsession-{UniqueSuffix()}@example.com";
        var oldPassword = ValidPassword();
        await RegisterAndLoginAsync(userName, email);

        var antiforgeryResponse = await _client.GetAsync("/account/antiforgery");
        Assert.That(antiforgeryResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var antiforgeryBody = await antiforgeryResponse.Content.ReadFromJsonAsync<AntiforgeryResponse>();
        Assert.That(antiforgeryBody, Is.Not.Null);
        var requestToken = antiforgeryBody!.Token;
        var antiforgeryCookie = antiforgeryResponse.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault()?.Split(';')[0].Trim()
            : null;

        var userId = await GetUserIdByUserNameAsync(userName);
        var token = await GeneratePasswordResetTokenAsync(userId);
        var resetResponse = await _client.PostAsJsonAsync(
            "/account/reset-password",
            new ResetPasswordRequest(email, token, "NewStrongPass123!"));
        Assert.That(resetResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "/account/logout");
        logoutRequest.Headers.Add("RequestVerificationToken", requestToken);
        if (!string.IsNullOrWhiteSpace(antiforgeryCookie))
        {
            logoutRequest.Headers.Add("Cookie", antiforgeryCookie);
        }

        var logoutResponse = await _client.SendAsync(logoutRequest);
        Assert.That(logoutResponse.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task ConfirmEmail_ActivatesPendingProjectMembership()
    {
        var ownerUserName = $"projowner{UniqueSuffix()}";
        var ownerEmail = $"projowner-{UniqueSuffix()}@example.com";
        await RegisterUserAsync(ownerUserName, ownerEmail, ValidPassword());
        var tenantId = await GetTenantIdForUserAsync(ownerUserName);
        var ownerId = await GetUserIdByUserNameAsync(ownerUserName);

        var projectName = $"Project {UniqueSuffix()}";
        var memberEmail = $"projmember-{UniqueSuffix()}@example.com";
        PlanDeckProject project;
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var provisioningAccessor = scope.ServiceProvider.GetRequiredService<IProvisioningContextAccessor>();
            provisioningAccessor.TenantId = tenantId;
            var repository = scope.ServiceProvider.GetRequiredService<IProjectRepository>();
            project = await repository.CreateAsync(projectName, null, ownerEmail, CancellationToken.None);
            await repository.InviteMemberAsync(project.Id, memberEmail, ProjectRole.Member, CancellationToken.None);
        }

        var memberUserName = $"projmember{UniqueSuffix()}";
        var memberPassword = ValidPassword();
        var memberUserId = await RegisterUserAsync(memberUserName, memberEmail, memberPassword, confirmEmail: false);

        var confirmResponse = await ConfirmEmailDirectAsync(memberUserId);
        Assert.That(confirmResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<PlanDeckDbContext>();
        var member = await db.ProjectMembers
            .IgnoreQueryFilters()
            .SingleAsync(m => m.ProjectId == project.Id && m.NormalizedEmail == memberEmail.ToUpperInvariant());
        Assert.Multiple(() =>
        {
            Assert.That(member.Status, Is.EqualTo(InvitationStatus.Accepted));
            Assert.That(member.AppUserId, Is.EqualTo(memberUserId));
            Assert.That(member.AcceptedAtUtc, Is.Not.Null);
        });
    }

    [Test]
    public async Task ConfirmEmail_ActivatesPendingTeamMembership()
    {
        var ownerUserName = $"teamowner{UniqueSuffix()}";
        var ownerEmail = $"teamowner-{UniqueSuffix()}@example.com";
        await RegisterUserAsync(ownerUserName, ownerEmail, ValidPassword());
        var tenantId = await GetTenantIdForUserAsync(ownerUserName);

        var memberEmail = $"teammember-{UniqueSuffix()}@example.com";
        Team team;
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var provisioningAccessor = scope.ServiceProvider.GetRequiredService<IProvisioningContextAccessor>();
            provisioningAccessor.TenantId = tenantId;
            var repository = scope.ServiceProvider.GetRequiredService<ITeamRepository>();
            team = await repository.CreateTeamAsync($"Team {UniqueSuffix()}", null, CancellationToken.None);
            await repository.AddMemberAsync(team.Id, memberEmail, null, CancellationToken.None);
        }

        var memberUserName = $"teammember{UniqueSuffix()}";
        var memberPassword = ValidPassword();
        var memberUserId = await RegisterUserAsync(memberUserName, memberEmail, memberPassword, confirmEmail: false);

        var confirmResponse = await ConfirmEmailDirectAsync(memberUserId);
        Assert.That(confirmResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        await using var verifyScope = _factory.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<PlanDeckDbContext>();
        var member = await db.TeamMembers
            .IgnoreQueryFilters()
            .SingleAsync(m => m.TeamId == team.Id && m.NormalizedEmail == memberEmail.ToUpperInvariant());
        Assert.Multiple(() =>
        {
            Assert.That(member.Status, Is.EqualTo(InvitationStatus.Accepted));
            Assert.That(member.AppUserId, Is.EqualTo(memberUserId));
            Assert.That(member.AcceptedAtUtc, Is.Not.Null);
        });
    }

    [Test]
    public async Task ConfirmEmail_CannotAcceptInvitationForAnotherEmail()
    {
        var ownerEmail = $"owner-other-{UniqueSuffix()}@example.com";
        var ownerUserName = $"ownerother{UniqueSuffix()}";
        await RegisterUserAsync(ownerUserName, ownerEmail, ValidPassword());
        var tenantId = await GetTenantIdForUserAsync(ownerUserName);
        var invitedEmail = $"invited-{UniqueSuffix()}@example.com";
        var token = Guid.NewGuid().ToString("N");
        await CreateInvitationAsync(tenantId, invitedEmail, token, TenantRole.Member);

        var otherEmail = $"other-{UniqueSuffix()}@example.com";
        var otherUserId = await RegisterUserAsync($"other{UniqueSuffix()}", otherEmail, ValidPassword(), confirmEmail: false);

        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(otherUserId.ToString());
        var confirmationToken = await userManager.GenerateEmailConfirmationTokenAsync(user!);
        var response = await _client.GetAsync(
            $"/api/account/confirm-email?userId={otherUserId}&token={Uri.EscapeDataString(confirmationToken)}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var invitation = await scope.ServiceProvider.GetRequiredService<PlanDeckDbContext>().TenantInvitations
            .IgnoreQueryFilters()
            .SingleAsync(i => i.TenantId == tenantId && i.NormalizedEmail == invitedEmail.ToUpperInvariant());
        Assert.That(invitation.Status, Is.EqualTo(InvitationStatus.Pending));
    }

    [Test]
    public async Task ForgotPassword_ForConfirmedAccount_WithoutEmailConfig_ReturnsSendFailed()
    {
        var email = $"forgot-confirmed-{UniqueSuffix()}@example.com";
        await RegisterUserAsync($"forgotconfirmed{UniqueSuffix()}", email, ValidPassword());

        var response = await _client.PostAsJsonAsync("/account/forgot-password", new ForgotPasswordRequest(email));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<AccountStatusResponse>();
        Assert.That(result!.Status, Is.EqualTo(ForgotPasswordStatus.SendFailed.ToString()));
    }

    private async Task<HttpResponseMessage> ConfirmEmailDirectAsync(Guid userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user!);
        return await _client.GetAsync(
            $"/api/account/confirm-email?userId={userId}&token={Uri.EscapeDataString(token)}");
    }

    private async Task<string> GeneratePasswordResetTokenAsync(Guid userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        return await userManager.GeneratePasswordResetTokenAsync(user!);
    }

    private async Task<Guid> GetUserIdByUserNameAsync(string userName)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanDeckDbContext>();
        var normalized = userName.ToUpperInvariant();
        var user = await db.Users.AsNoTracking()
            .SingleAsync(u => u.NormalizedUserName == normalized);
        return user.Id;
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

    private async Task<Guid> RegisterUserAsync(
        string userName,
        string email,
        string password,
        bool confirmEmail = true)
    {
        var request = new LocalRegisterRequest(email, "Test", "User", userName, password);
        var response = await _client.PostAsJsonAsync("/account/register", request);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Fail($"Registration failed with {response.StatusCode}: {body}");
        }

        var result = await response.Content.ReadFromJsonAsync<RegisterResponse>();
        var userId = result!.UserId!.Value;

        if (confirmEmail)
        {
            await ConfirmEmailAsync(userId);
        }

        return userId;
    }

    private async Task ConfirmEmailAsync(Guid userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        Assert.That(user, Is.Not.Null);
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user!);
        var response = await _client.GetAsync(
            $"/api/account/confirm-email?userId={userId}&token={Uri.EscapeDataString(token)}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
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

    private sealed record AccountStatusResponse(string Status, IReadOnlyList<string>? Errors = null);
}
