using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PlanDeck.Common.Localization;
using PlanDeck.Infrastructure.Identity;

namespace PlanDeck.Unit.Tests.Identity;

[TestFixture]
public sealed class SmtpEmailSenderTests
{
    [Test]
    public void EmailResources_Localizer_ReturnsHtmlBodyNotKeyName()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalization();
        var provider = services.BuildServiceProvider();
        var localizer = provider.GetRequiredService<IStringLocalizer<EmailResources>>();

        var html = localizer["Email_ConfirmationHtmlBody", "TestUser", "https://example.com/confirm"].Value;

        Assert.That(html, Does.Contain("<a href="));
        Assert.That(html, Does.Contain("TestUser"));
        Assert.That(html, Does.Not.Contain("Email_ConfirmationHtmlBody"));
    }

    [Test]
    public void SendConfirmationLinkAsync_MissingHost_ThrowsInvalidOperationException()
    {
        var sender = CreateSender(new EmailSettings
        {
            SenderAddress = "noreply@example.com",
            PublicBaseUrl = "https://example.com"
        });

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "testuser",
            Email = "test@example.com"
        };

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await sender.SendConfirmationLinkAsync(user, user.Email!, "https://example.com/confirm"));

        Assert.That(exception!.Message, Does.Contain("EmailSettings:Host"));
    }

    [Test]
    public void SendPasswordResetLinkAsync_MissingSenderAddress_ThrowsInvalidOperationException()
    {
        var sender = CreateSender(new EmailSettings
        {
            Host = "smtp.example.com",
            PublicBaseUrl = "https://example.com"
        });

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "testuser",
            Email = "test@example.com"
        };

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await sender.SendPasswordResetLinkAsync(user, user.Email!, "https://example.com/reset"));

        Assert.That(exception!.Message, Does.Contain("EmailSettings:SenderAddress"));
    }

    [Test]
    public void SendConfirmationLinkAsync_MissingPublicBaseUrl_ThrowsInvalidOperationException()
    {
        var sender = CreateSender(new EmailSettings
        {
            Host = "smtp.example.com",
            SenderAddress = "noreply@example.com"
        });

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "testuser",
            Email = "test@example.com"
        };

        var exception = Assert.ThrowsAsync<InvalidOperationException>(
            async () => await sender.SendConfirmationLinkAsync(user, user.Email!, "https://example.com/confirm"));

        Assert.That(exception!.Message, Does.Contain("EmailSettings:PublicBaseUrl"));
    }

    private static SmtpEmailSender CreateSender(EmailSettings settings) =>
        new(
            Options.Create(settings),
            new FakeStringLocalizer<EmailResources>(),
            NullLogger<SmtpEmailSender>.Instance,
            TimeProvider.System);

    private sealed class FakeStringLocalizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name] => new(name, name, false);

        public LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(name, arguments), false);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
