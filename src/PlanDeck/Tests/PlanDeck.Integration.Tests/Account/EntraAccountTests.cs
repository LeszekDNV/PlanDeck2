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
using ExternalLoginInfo = PlanDeck.Application.Account.ExternalLoginInfo;

namespace PlanDeck.Integration.Tests.Account;

[TestFixture]
public sealed class EntraAccountTests
{
    private WebApplicationFactory<Program> _factory = null!;

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
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _factory?.Dispose();
    }

    [Test]
    public async Task Register_CreatesOwnerWithExternalLogin()
    {
        var email = $"entra-owner-{UniqueSuffix()}@example.com";
        var loginInfo = CreateLoginInfo(email);
        var service = await CreateExternalAccountServiceAsync();

        var result = await service.RegisterAsync(loginInfo, null);

        Assert.That(result.Succeeded, Is.True);
        var appUser = await GetAppUserAsync(result.UserId!.Value);
        Assert.That(appUser, Is.Not.Null);
        Assert.That(appUser!.Role, Is.EqualTo(TenantRole.Owner));

        var logins = await GetLoginsAsync(result.UserId.Value);
        Assert.That(logins, Has.Count.EqualTo(1));
        Assert.That(logins[0].LoginProvider, Is.EqualTo("MicrosoftEntra"));
        Assert.That(logins[0].ProviderKey, Is.EqualTo(loginInfo.ProviderKey));
    }

    [Test]
    public async Task Register_WithExistingEmail_ReturnsDuplicateEmail()
    {
        var email = $"entra-dup-{UniqueSuffix()}@example.com";
        await RegisterLocalUserAsync($"entradup{UniqueSuffix()}", email, ValidPassword());

        var loginInfo = CreateLoginInfo(email);
        var service = await CreateExternalAccountServiceAsync();

        var result = await service.RegisterAsync(loginInfo, null);

        Assert.That(result.Status, Is.EqualTo(EntraCallbackStatus.DuplicateEmail));
    }

    [Test]
    public async Task Register_WithInvitation_CreatesMember()
    {
        var ownerEmail = $"entra-inv-owner-{UniqueSuffix()}@example.com";
        var ownerUserName = $"entrainvowner{UniqueSuffix()}";
        var ownerId = await RegisterLocalUserAsync(ownerUserName, ownerEmail, ValidPassword());
        var tenantId = await GetTenantIdForUserAsync(ownerUserName);

        var memberEmail = $"entra-inv-member-{UniqueSuffix()}@example.com";
        var token = Guid.NewGuid().ToString("N");
        await CreateInvitationAsync(tenantId, memberEmail, token, TenantRole.Member);

        var loginInfo = CreateLoginInfo(memberEmail);
        var service = await CreateExternalAccountServiceAsync();

        var result = await service.RegisterAsync(loginInfo, token);

        Assert.That(result.Succeeded, Is.True);
        var appUser = await GetAppUserAsync(result.UserId!.Value);
        Assert.That(appUser, Is.Not.Null);
        Assert.That(appUser!.TenantId, Is.EqualTo(tenantId));
        Assert.That(appUser.Role, Is.EqualTo(TenantRole.Member));
    }

    [Test]
    public async Task Login_WithLinkedIdentity_Succeeds()
    {
        var email = $"entra-login-{UniqueSuffix()}@example.com";
        var loginInfo = CreateLoginInfo(email);
        var service = await CreateExternalAccountServiceAsync();
        var registerResult = await service.RegisterAsync(loginInfo, null);
        Assert.That(registerResult.Succeeded, Is.True);

        var loginResult = await service.LoginAsync(loginInfo);

        Assert.That(loginResult.Succeeded, Is.True);
        Assert.That(loginResult.UserId, Is.EqualTo(registerResult.UserId));
    }

    [Test]
    public async Task Login_WithUnknownIdentity_ReturnsNotFound()
    {
        var loginInfo = CreateLoginInfo($"unknown-{UniqueSuffix()}@example.com");
        var service = await CreateExternalAccountServiceAsync();

        var result = await service.LoginAsync(loginInfo);

        Assert.That(result.Status, Is.EqualTo(EntraCallbackStatus.ExternalIdentityNotFound));
    }

    [Test]
    public async Task Login_DeactivatedAccount_ReturnsInactive()
    {
        var email = $"entra-inactive-{UniqueSuffix()}@example.com";
        var loginInfo = CreateLoginInfo(email);
        var service = await CreateExternalAccountServiceAsync();
        var registerResult = await service.RegisterAsync(loginInfo, null);
        Assert.That(registerResult.Succeeded, Is.True);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlanDeckDbContext>();
            var provisioningAccessor = scope.ServiceProvider.GetRequiredService<IProvisioningContextAccessor>();
            var appUser = await db.AppUsers.IgnoreQueryFilters()
                .SingleAsync(u => u.Id == registerResult.UserId!.Value);
            provisioningAccessor.TenantId = appUser.TenantId;
            appUser.IsActive = false;
            await db.SaveChangesAsync();
        }

        var loginResult = await service.LoginAsync(loginInfo);

        Assert.That(loginResult.Status, Is.EqualTo(EntraCallbackStatus.AccountInactive));
    }

    [Test]
    public async Task Link_AddsExternalLogin()
    {
        var localEmail = $"entra-link-local-{UniqueSuffix()}@example.com";
        var localUserName = $"entralinklocal{UniqueSuffix()}";
        var localUserId = await RegisterLocalUserAsync(localUserName, localEmail, ValidPassword());

        var entraEmail = $"entra-link-{UniqueSuffix()}@example.com";
        var loginInfo = CreateLoginInfo(entraEmail);
        var service = await CreateExternalAccountServiceAsync();

        var result = await service.LinkAsync(localUserId, loginInfo);

        Assert.That(result.Succeeded, Is.True);
        var logins = await GetLoginsAsync(localUserId);
        Assert.That(logins, Has.Count.EqualTo(1));
        Assert.That(logins[0].ProviderKey, Is.EqualTo(loginInfo.ProviderKey));
    }

    [Test]
    public async Task Link_CannotLinkAlreadyUsedIdentity()
    {
        var entraEmail = $"entra-link-used-{UniqueSuffix()}@example.com";
        var loginInfo = CreateLoginInfo(entraEmail);
        var service = await CreateExternalAccountServiceAsync();
        var registerResult = await service.RegisterAsync(loginInfo, null);
        Assert.That(registerResult.Succeeded, Is.True);

        var localEmail = $"entra-link-other-{UniqueSuffix()}@example.com";
        var localUserName = $"entralinkother{UniqueSuffix()}";
        var localUserId = await RegisterLocalUserAsync(localUserName, localEmail, ValidPassword());

        var result = await service.LinkAsync(localUserId, loginInfo);

        Assert.That(result.Status, Is.EqualTo(EntraCallbackStatus.ExternalIdentityUsedElsewhere));
    }

    [Test]
    public async Task Unlink_WithLocalPassword_Succeeds()
    {
        var localEmail = $"entra-unlink-{UniqueSuffix()}@example.com";
        var localUserName = $"entraunlink{UniqueSuffix()}";
        var localUserId = await RegisterLocalUserAsync(localUserName, localEmail, ValidPassword());

        var entraEmail = $"entra-unlink-ext-{UniqueSuffix()}@example.com";
        var loginInfo = CreateLoginInfo(entraEmail);
        var service = await CreateExternalAccountServiceAsync();
        var linkResult = await service.LinkAsync(localUserId, loginInfo);
        Assert.That(linkResult.Succeeded, Is.True);

        var unlinkResult = await service.UnlinkAsync(localUserId, loginInfo.Provider, loginInfo.ProviderKey);

        Assert.That(unlinkResult.Succeeded, Is.True);
        var logins = await GetLoginsAsync(localUserId);
        Assert.That(logins, Is.Empty);
    }

    [Test]
    public async Task Unlink_WithoutLocalPassword_Fails()
    {
        var entraEmail = $"entra-only-{UniqueSuffix()}@example.com";
        var loginInfo = CreateLoginInfo(entraEmail);
        var service = await CreateExternalAccountServiceAsync();
        var registerResult = await service.RegisterAsync(loginInfo, null);
        Assert.That(registerResult.Succeeded, Is.True);

        var result = await service.UnlinkAsync(registerResult.UserId!.Value, loginInfo.Provider, loginInfo.ProviderKey);

        Assert.That(result.Status, Is.EqualTo(EntraCallbackStatus.InvalidState));
    }

    [Test]
    public async Task SameOid_DifferentTenants_DoNotCollide()
    {
        var oid = Guid.NewGuid().ToString();
        var tid1 = Guid.NewGuid().ToString();
        var tid2 = Guid.NewGuid().ToString();
        var email1 = $"entra-mt1-{UniqueSuffix()}@example.com";
        var email2 = $"entra-mt2-{UniqueSuffix()}@example.com";

        var service = await CreateExternalAccountServiceAsync();
        var result1 = await service.RegisterAsync(new ExternalLoginInfo("MicrosoftEntra", $"{tid1}:{oid}", email1, "First", "Last"), null);
        var result2 = await service.RegisterAsync(new ExternalLoginInfo("MicrosoftEntra", $"{tid2}:{oid}", email2, "First", "Last"), null);

        Assert.That(result1.Succeeded, Is.True);
        Assert.That(result2.Succeeded, Is.True);
        Assert.That(result1.UserId, Is.Not.EqualTo(result2.UserId));
    }

    private async Task<IExternalAccountService> CreateExternalAccountServiceAsync()
    {
        var scope = _factory.Services.CreateAsyncScope();
        return scope.ServiceProvider.GetRequiredService<IExternalAccountService>();
    }

    private async Task<Guid> RegisterLocalUserAsync(string userName, string email, string password)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ILocalAccountService>();
        var result = await service.RegisterAsync(new LocalRegisterRequest(email, "Test", "User", userName, password));
        if (!result.Succeeded)
        {
            Assert.Fail($"Local registration failed: {result.Status} - {string.Join(", ", result.Errors ?? [])}");
        }

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(result.UserId!.Value.ToString());
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user!);
        var lifecycle = scope.ServiceProvider.GetRequiredService<IAccountLifecycleService>();
        await lifecycle.ConfirmEmailAsync(result.UserId.Value, token);

        return result.UserId.Value;
    }

    private async Task<AppUser?> GetAppUserAsync(Guid userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanDeckDbContext>();
        return await db.AppUsers.AsNoTracking().IgnoreQueryFilters()
            .SingleOrDefaultAsync(u => u.Id == userId);
    }

    private async Task<IList<UserLoginInfo>> GetLoginsAsync(Guid userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user is null ? [] : await userManager.GetLoginsAsync(user);
    }

    private async Task<Guid> GetTenantIdForUserAsync(string userName)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlanDeckDbContext>();
        var normalized = userName.ToUpperInvariant();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.NormalizedUserName == normalized);
        var appUser = await db.AppUsers.AsNoTracking().IgnoreQueryFilters().SingleAsync(u => u.Id == user.Id);
        return appUser.TenantId;
    }

    private async Task CreateInvitationAsync(Guid tenantId, string email, string token, TenantRole role)
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

    private static ExternalLoginInfo CreateLoginInfo(string email)
    {
        var tid = Guid.NewGuid().ToString();
        var oid = Guid.NewGuid().ToString();
        return new ExternalLoginInfo("MicrosoftEntra", $"{tid}:{oid}", email, "Test", "User");
    }

    private static string UniqueSuffix() => Guid.NewGuid().ToString("N")[..10];

    private static string ValidPassword() => "StrongPass123!";
}
