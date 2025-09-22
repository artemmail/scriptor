using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using YandexSpeech.models.DB;

namespace YandexSpeech.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IConfiguration _config;
        private readonly MyDbContext _dbContext;

        public AccountController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IConfiguration config,
            MyDbContext dbContext)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _config = config;
            _dbContext = dbContext;
        }

        // ---------- GOOGLE SIGN-IN ----------

        [AllowAnonymous]
        [HttpGet("signin-google")]
        public IActionResult SignInWithGoogle(string? returnUrl = null)
        {
            var redirectUrl = Url.Action(nameof(ExternalLoginCallback), "Account", new { returnUrl });
            var props = _signInManager.ConfigureExternalAuthenticationProperties("Google", redirectUrl);
            return Challenge(props, "Google");
        }

        [AllowAnonymous]
        [HttpGet("externallogincallback")]
        public async Task<IActionResult> ExternalLoginCallback(
            string? returnUrl = null,
            string? remoteError = null)
        {
            // --- ошибки провайдера ---
            if (!string.IsNullOrEmpty(remoteError))
            {
                Console.WriteLine($"ExternalLoginCallback error: {remoteError}");
                return Redirect($"{GetAngularRedirectUrl()}?error={Uri.EscapeDataString(remoteError)}");
            }

            // --- внешняя учётка ---
            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                Console.WriteLine("ExternalLoginCallback: NoExternalLoginInfo");
                return Redirect($"{GetAngularRedirectUrl()}?error=NoExternalLoginInfo");
            }

            // уже есть привязка?
            var signInResult = await _signInManager.ExternalLoginSignInAsync(
                info.LoginProvider, info.ProviderKey, isPersistent: false);

            ApplicationUser user;
            if (signInResult.Succeeded)
            {
                user = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            }
            else
            {
                // ----- первый логин -----
                var email = info.Principal.FindFirstValue(ClaimTypes.Email);
                if (string.IsNullOrEmpty(email))
                {
                    Console.WriteLine("ExternalLoginCallback: EmailNotFound");
                    return Redirect($"{GetAngularRedirectUrl()}?error=EmailNotFound");
                }

                user = await _userManager.FindByEmailAsync(email);
                if (user == null)
                {
                    user = new ApplicationUser
                    {
                        Email = email,
                        UserName = email,
                        IsSubscribed = false
                    };

                    var createRes = await _userManager.CreateAsync(user);
                    if (!createRes.Succeeded)
                    {
                        Console.WriteLine("ExternalLoginCallback: UserCreationFailed");
                        return Redirect($"{GetAngularRedirectUrl()}?error=UserCreationFailed");
                    }

                    user = await _userManager.FindByEmailAsync(email);
                }

                var addLoginRes = await _userManager.AddLoginAsync(user, info);
                if (!addLoginRes.Succeeded)
                {
                    Console.WriteLine("ExternalLoginCallback: ExternalLoginFailed");
                    return Redirect($"{GetAngularRedirectUrl()}?error=ExternalLoginFailed");
                }

                await _userManager.AddToRoleAsync(user, "Free");
            }

            // ---------- выдаём JWT + refresh ----------
            var accessToken = await GenerateJwtToken(user);
            var refreshValue = GenerateRefreshToken();
            var refreshLifetime = TimeSpan.FromDays(30);

            _dbContext.RefreshTokens.Add(new RefreshToken
            {
                Token = refreshValue,
                UserId = user.Id,
                Created = DateTime.UtcNow,
                Expires = DateTime.UtcNow + refreshLifetime
            });
            await _dbContext.SaveChangesAsync();

            Response.Cookies.Append("refreshToken", refreshValue, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Expires = DateTime.UtcNow + refreshLifetime
            });

            var redirect = $"{GetAngularRedirectUrl()}?token={accessToken}";
            if (!string.IsNullOrEmpty(returnUrl))
                redirect += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";

            return Redirect(redirect);
        }

        // ---------- LOGOUT ----------

        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var refreshTokenValue = Request.Cookies["refreshToken"];
            if (!string.IsNullOrEmpty(refreshTokenValue))
            {
                var tokenEntity = await _dbContext.RefreshTokens
                    .FirstOrDefaultAsync(rt => rt.Token == refreshTokenValue);
                if (tokenEntity != null)
                {
                    tokenEntity.IsRevoked = true;
                    await _dbContext.SaveChangesAsync();
                }
            }
            Response.Cookies.Delete("refreshToken");
            return NoContent();
        }

        // ---------- REFRESH ----------

        [HttpPost("refresh-token")]
        [AllowAnonymous]
        public async Task<IActionResult> RefreshToken()
        {
            var refreshTokenValue = Request.Cookies["refreshToken"];
            if (string.IsNullOrEmpty(refreshTokenValue))
                return Unauthorized("No refresh token");

            var tokenEntity = await _dbContext.RefreshTokens.FirstOrDefaultAsync(rt =>
                rt.Token == refreshTokenValue &&
                rt.Expires > DateTime.UtcNow &&
                !rt.IsRevoked);

            if (tokenEntity == null)
                return Unauthorized("Invalid refresh token");

            var user = await _userManager.FindByIdAsync(tokenEntity.UserId);
            if (user == null)
                return Unauthorized("User not found");

            tokenEntity.IsRevoked = true;

            var newAccessToken = await GenerateJwtToken(user);
            var newRefreshValue = GenerateRefreshToken();
            var refreshLifetime = TimeSpan.FromDays(30);

            _dbContext.RefreshTokens.Add(new RefreshToken
            {
                Token = newRefreshValue,
                UserId = user.Id,
                Created = DateTime.UtcNow,
                Expires = DateTime.UtcNow + refreshLifetime
            });
            await _dbContext.SaveChangesAsync();

            Response.Cookies.Append("refreshToken", newRefreshValue, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Expires = DateTime.UtcNow + refreshLifetime
            });

            return Ok(new { token = newAccessToken });
        }

        // ---------- ВСПОМОГАТЕЛЬНЫЕ ----------

        private static string GenerateRefreshToken()
            => $"{Guid.NewGuid():N}{Guid.NewGuid():N}";

        private string GetAngularRedirectUrl()
            => _config["Angular:RedirectUri"] ?? "/auth/callback";

        private async Task<string> GenerateJwtToken(ApplicationUser user)
        {
            var key = Encoding.UTF8.GetBytes(_config["Jwt:Key"]);
            var creds = new SigningCredentials(new SymmetricSecurityKey(key)
            { KeyId = _config["Jwt:KeyId"] }, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id),
                new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
                new(ClaimTypes.Name, user.UserName ?? ""),
                new("IsSubscribed", user.IsSubscribed.ToString())
            };
            if (user.SubscriptionExpiry.HasValue)
                claims.Add(new Claim("SubscriptionExpiry", user.SubscriptionExpiry.Value.ToString("o")));

            var roles = await _userManager.GetRolesAsync(user);
            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

            var token = new JwtSecurityToken(
                _config["Jwt:Issuer"],
                _config["Jwt:Audience"],
                claims,
                expires: DateTime.UtcNow.AddMinutes(60),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}