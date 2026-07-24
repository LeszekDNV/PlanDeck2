using System.Net;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using PlanDeck.Common.Localization;

namespace PlanDeck.Infrastructure.Identity;

public sealed class SmtpEmailSender(
    IOptions<EmailSettings> optionsAccessor,
    IStringLocalizer<EmailResources> localizer,
    ILogger<SmtpEmailSender> logger,
    TimeProvider timeProvider) : IEmailSender<ApplicationUser>
{
    private readonly EmailSettings _settings = optionsAccessor.Value;

    public Task SendConfirmationLinkAsync(
        ApplicationUser user,
        string email,
        string confirmationLink) =>
        SendTemplatedAsync(
            email,
            localizer["Email_ConfirmationSubject"].Value,
            localizer["Email_ConfirmationBody", GetDisplayName(user), confirmationLink].Value,
            localizer["Email_ConfirmationHtmlBody", WebUtility.HtmlEncode(GetDisplayName(user)), WebUtility.HtmlEncode(confirmationLink)].Value,
            "confirmation");

    public Task SendPasswordResetLinkAsync(
        ApplicationUser user,
        string email,
        string resetLink) =>
        SendTemplatedAsync(
            email,
            localizer["Email_PasswordResetSubject"].Value,
            localizer["Email_PasswordResetBody", GetDisplayName(user), resetLink].Value,
            localizer["Email_PasswordResetHtmlBody", WebUtility.HtmlEncode(GetDisplayName(user)), WebUtility.HtmlEncode(resetLink)].Value,
            "password-reset");

    public Task SendPasswordResetCodeAsync(
        ApplicationUser user,
        string email,
        string resetCode) =>
        SendPasswordResetLinkAsync(user, email, resetCode);

    private static string GetDisplayName(ApplicationUser user) =>
        string.IsNullOrWhiteSpace(user.UserName)
            ? (user.Email ?? string.Empty)
            : user.UserName;

    private async Task SendTemplatedAsync(
        string email,
        string subject,
        string textBody,
        string htmlBody,
        string purpose)
    {
        ValidateSettings();

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_settings.SenderName, _settings.SenderAddress));
        message.To.Add(MailboxAddress.Parse(email));
        message.Subject = subject;
        message.Body = new BodyBuilder
        {
            TextBody = textBody,
            HtmlBody = htmlBody,
        }.ToMessageBody();

        var attempt = 0;
        Exception? lastException = null;

        while (attempt <= _settings.RetryCount)
        {
            using var client = new SmtpClient();
            try
            {
                await client.ConnectAsync(
                    _settings.Host,
                    _settings.Port,
                    _settings.UseTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(_settings.Username))
                {
                    await client.AuthenticateAsync(_settings.Username!, _settings.Password ?? string.Empty).ConfigureAwait(false);
                }

                await client.SendAsync(message).ConfigureAwait(false);
                await client.DisconnectAsync(true).ConfigureAwait(false);
                return;
            }
            catch (Exception exception) when (attempt < _settings.RetryCount && IsTransient(exception))
            {
                lastException = exception;
                attempt++;
                logger.LogWarning(
                    exception,
                    "Email send attempt {Attempt} of {Max} failed for {Purpose} to {Email}.",
                    attempt,
                    _settings.RetryCount + 1,
                    purpose,
                    email);
                await Task.Delay(_settings.RetryDelay, timeProvider, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Email send failed for {Purpose} to {Email}.",
                    purpose,
                    email);
                throw new InvalidOperationException($"Failed to send {purpose} email.", exception);
            }
        }

        if (lastException is not null)
        {
            throw new InvalidOperationException($"Failed to send {purpose} email after {_settings.RetryCount + 1} attempts.", lastException);
        }
    }

    private void ValidateSettings()
    {
        if (string.IsNullOrWhiteSpace(_settings.Host))
        {
            throw new InvalidOperationException("EmailSettings:Host is required.");
        }

        if (string.IsNullOrWhiteSpace(_settings.SenderAddress))
        {
            throw new InvalidOperationException("EmailSettings:SenderAddress is required.");
        }

        if (string.IsNullOrWhiteSpace(_settings.PublicBaseUrl))
        {
            throw new InvalidOperationException("EmailSettings:PublicBaseUrl is required.");
        }
    }

    private static bool IsTransient(Exception exception) =>
        exception is SmtpCommandException
            or SmtpProtocolException
            or IOException
            or OperationCanceledException;
}
