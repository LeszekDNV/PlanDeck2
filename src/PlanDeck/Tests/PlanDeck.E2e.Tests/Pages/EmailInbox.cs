using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace PlanDeck.E2e.Tests.Pages;

public sealed class EmailInbox
{
    private static readonly Regex ConfirmLinkRegex = new(
        "https?://[^\\s\"'<>]+/account/confirm-email\\?[^\\s\"'<>]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient _client;

    public EmailInbox(string baseUrl = "http://localhost:1080")
    {
        _client = new HttpClient { BaseAddress = new Uri(baseUrl) };
    }

    public async Task<string> WaitForConfirmationLinkAsync(string recipientEmail, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var listing = await _client.GetFromJsonAsync<MessageListResponse>("/api/v1/messages");
            var message = listing?.Messages?
                .FirstOrDefault(m => m.To?.Any(t => string.Equals(t.Address, recipientEmail, StringComparison.OrdinalIgnoreCase)) == true);

            if (message?.Id is not null)
            {
                var payload = await _client.GetStringAsync($"/api/v1/message/{message.Id}");
                var match = ConfirmLinkRegex.Match(payload);
                if (match.Success)
                {
                    return System.Net.WebUtility.HtmlDecode(match.Value);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new TimeoutException($"Confirmation email for '{recipientEmail}' was not found in MailPit within {timeout.TotalSeconds:F0}s.");
    }

    private sealed class MessageListResponse
    {
        public List<MessageSummary> Messages { get; set; } = [];
    }

    private sealed class MessageSummary
    {
        public string? Id { get; set; }
        public List<MailAddress> To { get; set; } = [];
    }

    private sealed class MailAddress
    {
        public string? Address { get; set; }
    }
}
