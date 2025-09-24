using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace YandexSpeech.Extensions
{
    public static class ClaimsPrincipalExtensions
    {
        public static string? GetUserId(this ClaimsPrincipal principal)
        {
            if (principal == null)
            {
                return null;
            }

            return principal.FindFirstValue(ClaimTypes.NameIdentifier)
                   ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        }
    }
}
