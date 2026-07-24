using Microsoft.IdentityModel.Tokens;

namespace PlanDeck.Infrastructure.Identity;

public static class EntraIssuerValidator
{
    public static string Validate(string issuer, SecurityToken token, TokenValidationParameters parameters)
    {
        if (Uri.TryCreate(issuer, UriKind.Absolute, out var issuerUri)
            && string.Equals(issuerUri.Host, "login.microsoftonline.com", StringComparison.OrdinalIgnoreCase)
            && issuerUri.AbsolutePath.TrimEnd('/').EndsWith("/v2.0", StringComparison.OrdinalIgnoreCase)
            && IsTenantGuidSegment(issuerUri.AbsolutePath))
        {
            return issuer;
        }

        throw new SecurityTokenInvalidIssuerException($"Invalid issuer '{issuer}'.");
    }

    private static bool IsTenantGuidSegment(string absolutePath)
    {
        var trimmed = absolutePath.Trim('/');
        var segments = trimmed.Split('/');
        if (segments.Length < 2)
        {
            return false;
        }

        var tenantSegment = segments[0];
        return Guid.TryParse(tenantSegment, out _);
    }
}
