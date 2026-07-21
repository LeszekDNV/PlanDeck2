using System.Net;
using System.Net.Http.Headers;
using System.Text;
using PlanDeck.Application.Abstractions;

namespace PlanDeck.Infrastructure.AzureDevOps;

public sealed class AzureDevOpsConnectionValidator(HttpClient httpClient)
    : IAzureDevOpsConnectionValidator
{
    public async Task ValidateAsync(
        AzureDevOpsConnectionValidationRequest request,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Get,
            $"{request.OrganizationUrl.TrimEnd('/')}/_apis/projects/{Uri.EscapeDataString(request.Project)}?api-version=7.1");
        var token = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($":{request.PersonalAccessToken}"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new AzureDevOpsConnectionValidationException(
                AzureDevOpsConnectionValidationFailure.Unavailable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AzureDevOpsConnectionValidationException(
                AzureDevOpsConnectionValidationFailure.Unavailable);
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            throw new AzureDevOpsConnectionValidationException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized =>
                    AzureDevOpsConnectionValidationFailure.InvalidCredentials,
                HttpStatusCode.Forbidden =>
                    AzureDevOpsConnectionValidationFailure.Forbidden,
                HttpStatusCode.NotFound =>
                    AzureDevOpsConnectionValidationFailure.ProjectNotFound,
                _ => AzureDevOpsConnectionValidationFailure.Unavailable
            });
        }
    }
}
