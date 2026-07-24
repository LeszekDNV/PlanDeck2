using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PlanDeck.Common.Identity;
using PlanDeck.Infrastructure.Persistence;

namespace PlanDeck.Infrastructure.Identity;

public sealed class PlanDeckUserClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    IOptions<IdentityOptions> optionsAccessor,
    PlanDeckDbContext db) : UserClaimsPrincipalFactory<ApplicationUser>(userManager, optionsAccessor)
{
    public override async Task<ClaimsPrincipal> CreateAsync(ApplicationUser user)
    {
        var principal = await base.CreateAsync(user);
        var identity = (ClaimsIdentity?)principal.Identity;
        if (identity is null)
        {
            return principal;
        }

        identity.AddClaim(new Claim(PlanDeckClaimTypes.UserId, user.Id.ToString()));
        identity.AddClaim(new Claim(PlanDeckClaimTypes.ParticipantId, user.Id.ToString()));
        identity.AddClaim(new Claim(PlanDeckClaimTypes.ActiveUser, bool.FalseString));

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            identity.AddClaim(new Claim("email", user.Email));
        }

        if (!string.IsNullOrWhiteSpace(user.UserName))
        {
            identity.AddClaim(new Claim("name", user.UserName));
        }

        var appUser = await db.AppUsers.AsNoTracking()
            .SingleOrDefaultAsync(u => u.Id == user.Id);

        if (appUser is not null && appUser.IsActive)
        {
            identity.AddClaim(new Claim(PlanDeckClaimTypes.MemberTenantId, appUser.TenantId.ToString()));
            identity.AddClaim(new Claim(PlanDeckClaimTypes.TenantRole, appUser.Role.ToString()));

            var nameClaim = identity.FindFirst("name");
            if (nameClaim is not null)
            {
                identity.RemoveClaim(nameClaim);
            }

            identity.AddClaim(new Claim("name", $"{appUser.FirstName} {appUser.LastName}".Trim()));
            var activeClaim = identity.FindFirst(PlanDeckClaimTypes.ActiveUser);
            if (activeClaim is not null)
            {
                identity.RemoveClaim(activeClaim);
            }

            identity.AddClaim(new Claim(PlanDeckClaimTypes.ActiveUser, bool.TrueString));
        }

        return principal;
    }
}

