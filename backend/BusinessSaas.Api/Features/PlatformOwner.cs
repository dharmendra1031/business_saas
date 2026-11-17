using System.Security.Claims;

namespace BusinessSaas.Api.Features;

public static class PlatformOwner
{
    public static bool IsOwner(ClaimsPrincipal principal, IConfiguration configuration) =>
        principal.Identity?.IsAuthenticated == true &&
        long.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) &&
        IsOwnerId(userId, configuration);

    public static bool IsOwnerId(long userId, IConfiguration configuration) =>
        userId > 0 &&
        long.TryParse(configuration["Platform:OwnerUserId"], out var ownerId) &&
        ownerId > 0 && userId == ownerId;
}
