using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using PlanDeck.Application.Abstractions;

namespace PlanDeck.Infrastructure.AzureDevOps;

public sealed class AzureDevOpsWorkItemClient(HttpClient httpClient, IOptions<AzureDevOpsOptions> options) : IAzureDevOpsWorkItemClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AzureDevOpsOptions _options = options.Value;

    public async Task<IReadOnlyCollection<AzureDevOpsWorkItem>> ImportWorkItemsAsync(AzureDevOpsImportRequest request, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        var limit = request.Limit <= 0 ? 100 : Math.Min(request.Limit, 200);
        var whereClause = string.IsNullOrWhiteSpace(request.WiqlWhereClause)
            ? "[System.WorkItemType] IN ('User Story', 'Product Backlog Item', 'Bug', 'Task')"
            : request.WiqlWhereClause;
        var wiql = new
        {
            query = $"""
                SELECT [System.Id]
                FROM WorkItems
                WHERE [System.TeamProject] = @project
                  AND {whereClause}
                ORDER BY [System.ChangedDate] DESC
                """
        };

        using var wiqlRequest = CreateRequest(HttpMethod.Post, $"{ProjectBaseUrl}/_apis/wit/wiql?api-version=7.2-preview.2");
        wiqlRequest.Content = JsonContent(wiql);
        using var wiqlResponse = await SendAsync(wiqlRequest, cancellationToken);
        await using var wiqlStream = await wiqlResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var wiqlDocument = await JsonDocument.ParseAsync(wiqlStream, cancellationToken: cancellationToken);

        if (!wiqlDocument.RootElement.TryGetProperty("workItems", out var workItemsElement))
        {
            return [];
        }

        var ids = workItemsElement.EnumerateArray()
            .Select(item => item.GetProperty("id").GetInt32())
            .Take(limit)
            .ToArray();

        if (ids.Length == 0)
        {
            return [];
        }

        var fields = new[]
        {
            "System.Id",
            "System.Title",
            "System.State",
            "System.WorkItemType",
            _options.EstimateField,
            _options.DescriptionField,
            _options.AcceptanceCriteriaField
        };

        using var batchRequest = CreateRequest(HttpMethod.Post, $"{OrganizationBaseUrl}/_apis/wit/workitemsbatch?api-version=7.2-preview.1");
        batchRequest.Content = JsonContent(new { ids, fields, errorPolicy = "Omit" });
        using var batchResponse = await SendAsync(batchRequest, cancellationToken);
        await using var batchStream = await batchResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var batchDocument = await JsonDocument.ParseAsync(batchStream, cancellationToken: cancellationToken);

        return batchDocument.RootElement.GetProperty("value")
            .EnumerateArray()
            .Select(ParseWorkItem)
            .ToArray();
    }

    public async Task<AzureDevOpsWriteEstimateResult> WriteEstimateAsync(AzureDevOpsWriteEstimateRequest request, CancellationToken cancellationToken)
    {
        EnsureConfigured();

        if (request.WorkItemId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Work item ID must be positive.");
        }

        var patchOperations = new List<object>();
        if (request.ExpectedRevision.HasValue)
        {
            patchOperations.Add(new { op = "test", path = "/rev", value = request.ExpectedRevision.Value });
        }

        patchOperations.Add(new { op = "add", path = $"/fields/{_options.EstimateField}", value = request.Estimate });

        using var writeRequest = CreateRequest(new HttpMethod("PATCH"), $"{OrganizationBaseUrl}/_apis/wit/workitems/{request.WorkItemId}?api-version=7.2-preview.3");
        writeRequest.Content = new StringContent(JsonSerializer.Serialize(patchOperations, JsonOptions), Encoding.UTF8, "application/json-patch+json");
        using var response = await SendAsync(writeRequest, cancellationToken);
        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var responseDocument = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);

        return new AzureDevOpsWriteEstimateResult(
            responseDocument.RootElement.GetProperty("id").GetInt32(),
            responseDocument.RootElement.GetProperty("rev").GetInt32());
    }

    private AzureDevOpsWorkItem ParseWorkItem(JsonElement item)
    {
        var fields = item.GetProperty("fields");
        return new AzureDevOpsWorkItem(
            item.GetProperty("id").GetInt32(),
            GetFieldString(fields, "System.Title"),
            GetFieldString(fields, "System.State"),
            GetFieldString(fields, "System.WorkItemType"),
            item.TryGetProperty("rev", out var rev) ? rev.GetInt32() : 0,
            GetFieldDouble(fields, _options.EstimateField),
            BuildDescription(fields));
    }

    private string? BuildDescription(JsonElement fields)
    {
        var description = HtmlToText(GetFieldString(fields, _options.DescriptionField));
        var acceptance = HtmlToText(GetFieldString(fields, _options.AcceptanceCriteriaField));

        var builder = new StringBuilder();
        if (description.Length > 0)
        {
            builder.Append(description);
        }

        if (acceptance.Length > 0)
        {
            if (builder.Length > 0)
            {
                builder.Append("\n\n");
            }

            builder.Append("**Acceptance Criteria**\n\n").Append(acceptance);
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static string HtmlToText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var text = html;
        text = Regex.Replace(text, "<li[^>]*>", "- ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "</(p|div|li|ul|ol|tr|h[1-6])>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", string.Empty);
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, "[ \\t]+\n", "\n");
        text = Regex.Replace(text, "\n{3,}", "\n\n");
        return text.Trim();
    }

    private static string GetFieldString(JsonElement fields, string name)
    {
        return fields.TryGetProperty(name, out var value) ? value.GetString() ?? string.Empty : string.Empty;
    }

    private static double? GetFieldDouble(JsonElement fields, string name)
    {
        if (!fields.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out var number) => number,
            JsonValueKind.String when double.TryParse(value.GetString(), out var number) => number,
            _ => null
        };
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta?.ToString()
                ?? response.Headers.RetryAfter?.Date?.ToString("O")
                ?? "unspecified";
            throw new InvalidOperationException($"Azure DevOps rate limit reached. Retry-After: {retryAfter}.");
        }

        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
        {
            throw new InvalidOperationException("Azure DevOps work item revision changed before write-back completed.");
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"Azure DevOps request failed with {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }

        return response;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{_options.PersonalAccessToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static StringContent JsonContent<T>(T value)
    {
        return new StringContent(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, "application/json");
    }

    private string OrganizationBaseUrl => _options.OrganizationUrl.TrimEnd('/');

    private string ProjectBaseUrl => $"{OrganizationBaseUrl}/{Uri.EscapeDataString(_options.Project)}";

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.OrganizationUrl)
            || string.IsNullOrWhiteSpace(_options.Project)
            || string.IsNullOrWhiteSpace(_options.EstimateField)
            || string.IsNullOrWhiteSpace(_options.PersonalAccessToken))
        {
            throw new InvalidOperationException("Azure DevOps integration requires OrganizationUrl, Project, EstimateField, and PersonalAccessToken configuration.");
        }
    }
}
