using Microsoft.IdentityModel.Tokens;
using PlanDeck.Infrastructure.Identity;

namespace PlanDeck.Unit.Tests.Identity;

[TestFixture]
public sealed class EntraIssuerValidatorTests
{
    [Test]
    public void Validate_AcceptsTenantGuidIssuer()
    {
        var issuer = "https://login.microsoftonline.com/12345678-1234-1234-1234-123456789012/v2.0";

        var result = EntraIssuerValidator.Validate(issuer, null!, new TokenValidationParameters());

        Assert.That(result, Is.EqualTo(issuer));
    }

    [Test]
    public void Validate_RejectsCommonIssuer()
    {
        var issuer = "https://login.microsoftonline.com/common/v2.0";

        Assert.Throws<SecurityTokenInvalidIssuerException>(() =>
            EntraIssuerValidator.Validate(issuer, null!, new TokenValidationParameters()));
    }

    [Test]
    public void Validate_RejectsOrganizationsIssuer()
    {
        var issuer = "https://login.microsoftonline.com/organizations/v2.0";

        Assert.Throws<SecurityTokenInvalidIssuerException>(() =>
            EntraIssuerValidator.Validate(issuer, null!, new TokenValidationParameters()));
    }

    [Test]
    public void Validate_RejectsInvalidHost()
    {
        var issuer = "https://malicious.example.com/12345678-1234-1234-1234-123456789012/v2.0";

        Assert.Throws<SecurityTokenInvalidIssuerException>(() =>
            EntraIssuerValidator.Validate(issuer, null!, new TokenValidationParameters()));
    }

    [Test]
    public void Validate_RejectsNonV2Path()
    {
        var issuer = "https://login.microsoftonline.com/12345678-1234-1234-1234-123456789012";

        Assert.Throws<SecurityTokenInvalidIssuerException>(() =>
            EntraIssuerValidator.Validate(issuer, null!, new TokenValidationParameters()));
    }
}
