using System.Security.Claims;

namespace AnalyticDashboard.Api.Auth;

public static class ClaimsPrincipalExtensions
{
    public static bool TryGetUserId(
        this ClaimsPrincipal user,
        out Guid userId)
    {
        var userIdClaim = user.FindFirst(
            ClaimTypes.NameIdentifier
        )?.Value;

        return Guid.TryParse(userIdClaim, out userId)
               && userId != Guid.Empty;
    }
}
