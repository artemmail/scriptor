using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using YandexSpeech.models.DB;
using YandexSpeech.models.DTO;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using YandexSpeech.services.Interface;

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
        private readonly ISubscriptionService _subscriptionService;

        public AccountController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IConfiguration config,
            MyDbContext dbContext,
            ISubscriptionService subscriptionService)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _config = config;
            _dbContext = dbContext;
            _subscriptionService = subscriptionService ?? throw new ArgumentNullException(nameof(subscriptionService));
        }

        // ---------- EXTERNAL SIGN-IN ----------

        [AllowAnonymous]
        [HttpGet("signin-google")]
        public IActionResult SignInWithGoogle(string? returnUrl = null)
            => SignInWithProvider("Google", returnUrl);

        [AllowAnonymous]
        [HttpGet("signin-yandex")]
        public IActionResult SignInWithYandex(string? returnUrl = null)
            => SignInWithProvider("Yandex", returnUrl);

        [AllowAnonymous]
        [HttpGet("mobile/signin-google")]
        public IActionResult MobileSignInWithGoogle([FromQuery] string redirectUri, string? returnUrl = null)
        {
            if (!TryValidateMobileRedirectUri(redirectUri, out _))
            {
                return BadRequest("Invalid mobile redirect URI.");
            }

            return SignInWithProvider("Google", returnUrl, redirectUri);
        }

        [AllowAnonymous]
        [HttpGet("mobile/signin-yandex")]
        public IActionResult MobileSignInWithYandex([FromQuery] string redirectUri, string? returnUrl = null)
        {
            if (!TryValidateMobileRedirectUri(redirectUri, out _))
            {
                return BadRequest("Invalid mobile redirect URI.");
            }

            return SignInWithProvider("Yandex", returnUrl, redirectUri);
        }

        [AllowAnonymous]
        [HttpGet("externallogincallback")]
        public async Task<IActionResult> ExternalLoginCallback(
            string? returnUrl = null,
            string? remoteError = null)
        {
            if (!string.IsNullOrEmpty(remoteError))
            {
                Console.WriteLine($"ExternalLoginCallback error: {remoteError}");
                return Redirect(AppendQueryParameter(GetAngularRedirectUrl(), "error", remoteError));
            }

            var result = await CompleteExternalLoginAsync();
            if (!result.Success || result.User == null)
            {
                return Redirect(AppendQueryParameter(GetAngularRedirectUrl(), "error", result.Error ?? "ExternalLoginFailed"));
            }

            var tokens = await IssueMobileAuthResultAsync(result.User, revokeExistingToken: null);
            Response.Cookies.Append("refreshToken", tokens.RefreshToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Expires = DateTime.UtcNow + TimeSpan.FromDays(30)
            });

            var redirect = AppendQueryParameter(GetAngularRedirectUrl(), "token", tokens.AccessToken);
            if (!string.IsNullOrEmpty(returnUrl))
            {
                redirect = AppendQueryParameter(redirect, "returnUrl", returnUrl);
            }

            return Redirect(redirect);
        }

        [AllowAnonymous]
        [HttpGet("mobile/externallogincallback")]
        public async Task<IActionResult> MobileExternalLoginCallback(
            [FromQuery] string redirectUri,
            string? returnUrl = null,
            string? remoteError = null)
        {
            if (!TryValidateMobileRedirectUri(redirectUri, out var safeRedirectUri))
            {
                return BadRequest("Invalid mobile redirect URI.");
            }

            if (!string.IsNullOrEmpty(remoteError))
            {
                return Redirect(AppendQueryParameter(safeRedirectUri, "error", remoteError));
            }

            var result = await CompleteExternalLoginAsync();
            if (!result.Success || result.User == null)
            {
                return Redirect(AppendQueryParameter(safeRedirectUri, "error", result.Error ?? "ExternalLoginFailed"));
            }

            var tokens = await IssueMobileAuthResultAsync(result.User, revokeExistingToken: null);

            var callbackUri = AppendQueryParameter(safeRedirectUri, "token", tokens.AccessToken);
            callbackUri = AppendQueryParameter(callbackUri, "refreshToken", tokens.RefreshToken);
            callbackUri = AppendQueryParameter(callbackUri, "userId", tokens.User.Id);
            callbackUri = AppendQueryParameter(callbackUri, "displayName", tokens.User.DisplayName);
            callbackUri = AppendQueryParameter(callbackUri, "email", tokens.User.Email);

            if (!string.IsNullOrWhiteSpace(returnUrl))
            {
                callbackUri = AppendQueryParameter(callbackUri, "returnUrl", returnUrl);
            }

            return Redirect(callbackUri);
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

        [HttpGet("profile")]
        [AllowAnonymous]
        public async Task<ActionResult<UserProfileDto>> GetProfile()
        {
            if (User?.Identity == null || !User.Identity.IsAuthenticated)
            {
                return Ok(null);
            }

            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId))
            {
                return Ok(null);
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return Ok(null);
            }

            await EnsureDisplayNameAsync(user);
            await EnsureFinancialProfileAsync(user);
            await _subscriptionService.EnsureWelcomePackageAsync(user.Id);

            return Ok(new UserProfileDto
            {
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                DisplayName = user.DisplayName
            });
        }

        [HttpPut("profile")]
        public async Task<ActionResult<UserProfileDto>> UpdateProfile([FromBody] UpdateProfileRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var userId = _userManager.GetUserId(User);
            if (userId == null)
            {
                return Unauthorized();
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return Unauthorized();
            }

            var displayName = request.DisplayName.Trim();
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return BadRequest("Display name is required.");
            }

            user.DisplayName = displayName;
            var updateResult = await _userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, "Failed to update profile.");
            }

            return Ok(new UserProfileDto
            {
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                DisplayName = user.DisplayName
            });
        }

        // ---------- REFRESH ----------

        [HttpPost("refresh-token")]
        [AllowAnonymous]
        public async Task<IActionResult> RefreshToken()
        {
            var refreshTokenValue = Request.Cookies["refreshToken"];
            if (string.IsNullOrEmpty(refreshTokenValue))
                return Unauthorized("No refresh token");

            var tokenEntity = await FindValidRefreshTokenAsync(refreshTokenValue);

            if (tokenEntity == null)
                return Unauthorized("Invalid refresh token");

            var user = await _userManager.FindByIdAsync(tokenEntity.UserId);
            if (user == null)
                return Unauthorized("User not found");

            var tokens = await IssueMobileAuthResultAsync(user, tokenEntity);

            Response.Cookies.Append("refreshToken", tokens.RefreshToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                Expires = DateTime.UtcNow + TimeSpan.FromDays(30)
            });

            return Ok(new { token = tokens.AccessToken });
        }

        [AllowAnonymous]
        [HttpPost("mobile/refresh")]
        public async Task<ActionResult<MobileAuthResultDto>> MobileRefreshToken([FromBody] MobileRefreshTokenRequest request)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            var tokenEntity = await FindValidRefreshTokenAsync(request.RefreshToken);
            if (tokenEntity == null)
            {
                return Unauthorized("Invalid refresh token");
            }

            var user = await _userManager.FindByIdAsync(tokenEntity.UserId);
            if (user == null)
            {
                return Unauthorized("User not found");
            }

            return Ok(await IssueMobileAuthResultAsync(user, tokenEntity));
        }

        [AllowAnonymous]
        [HttpPost("mobile/logout")]
        public async Task<IActionResult> MobileLogout([FromBody] MobileLogoutRequest? request)
        {
            var tokenValue = request?.RefreshToken;
            if (string.IsNullOrWhiteSpace(tokenValue))
            {
                return NoContent();
            }

            var tokenEntity = await _dbContext.RefreshTokens
                .FirstOrDefaultAsync(rt => rt.Token == tokenValue);

            if (tokenEntity != null && !tokenEntity.IsRevoked)
            {
                tokenEntity.IsRevoked = true;
                await _dbContext.SaveChangesAsync();
            }

            return NoContent();
        }

        // ---------- ВСПОМОГАТЕЛЬНЫЕ ----------

        private IActionResult SignInWithProvider(string provider, string? returnUrl, string? mobileRedirectUri = null)
        {
            var redirectAction = string.IsNullOrWhiteSpace(mobileRedirectUri)
                ? nameof(ExternalLoginCallback)
                : nameof(MobileExternalLoginCallback);

            object routeValues = string.IsNullOrWhiteSpace(mobileRedirectUri)
                ? new { returnUrl }
                : new { returnUrl, redirectUri = mobileRedirectUri };

            var redirectUrl = Url.Action(redirectAction, "Account", routeValues);
            var props = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
            if (!string.IsNullOrWhiteSpace(mobileRedirectUri))
            {
                props.Items["mobileRedirectUri"] = mobileRedirectUri;
            }

            return Challenge(props, provider);
        }

        private static string GenerateRefreshToken()
            => $"{Guid.NewGuid():N}{Guid.NewGuid():N}";

        private string GetAngularRedirectUrl()
            => _config["Angular:RedirectUri"] ?? "/auth/callback";

        private async Task<ExternalLoginCompletionResult> CompleteExternalLoginAsync()
        {
            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                Console.WriteLine("ExternalLoginCallback: NoExternalLoginInfo");
                return ExternalLoginCompletionResult.Failed("NoExternalLoginInfo");
            }

            var signInResult = await _signInManager.ExternalLoginSignInAsync(
                info.LoginProvider, info.ProviderKey, isPersistent: false);

            ApplicationUser? user;
            if (signInResult.Succeeded)
            {
                user = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            }
            else
            {
                var email = info.Principal.FindFirstValue(ClaimTypes.Email)
                            ?? info.Principal.FindFirstValue("urn:yandex:email");
                if (string.IsNullOrEmpty(email))
                {
                    Console.WriteLine("ExternalLoginCallback: EmailNotFound");
                    return ExternalLoginCompletionResult.Failed("EmailNotFound");
                }

                user = await _userManager.FindByEmailAsync(email);
                if (user == null)
                {
                    var displayName = info.Principal.FindFirstValue(ClaimTypes.Name)
                        ?? info.Principal.FindFirstValue("urn:yandex:login")
                        ?? GenerateDefaultDisplayName();

                    user = new ApplicationUser
                    {
                        Email = email,
                        UserName = email,
                        DisplayName = displayName
                    };

                    var createRes = await _userManager.CreateAsync(user);
                    if (!createRes.Succeeded)
                    {
                        Console.WriteLine("ExternalLoginCallback: UserCreationFailed");
                        return ExternalLoginCompletionResult.Failed("UserCreationFailed");
                    }

                    user = await _userManager.FindByEmailAsync(email);
                }

                var addLoginRes = await _userManager.AddLoginAsync(user!, info);
                if (!addLoginRes.Succeeded)
                {
                    Console.WriteLine("ExternalLoginCallback: ExternalLoginFailed");
                    return ExternalLoginCompletionResult.Failed("ExternalLoginFailed");
                }

                await _userManager.AddToRoleAsync(user!, "Free");
            }

            if (user == null)
            {
                return ExternalLoginCompletionResult.Failed("UserNotFound");
            }

            await EnsureDisplayNameAsync(user);
            await EnsureFinancialProfileAsync(user);
            await _subscriptionService.EnsureWelcomePackageAsync(user.Id);

            return ExternalLoginCompletionResult.Completed(user);
        }

        private async Task<RefreshToken?> FindValidRefreshTokenAsync(string? refreshTokenValue)
        {
            if (string.IsNullOrWhiteSpace(refreshTokenValue))
            {
                return null;
            }

            return await _dbContext.RefreshTokens.FirstOrDefaultAsync(rt =>
                rt.Token == refreshTokenValue &&
                rt.Expires > DateTime.UtcNow &&
                !rt.IsRevoked);
        }

        private async Task<MobileAuthResultDto> IssueMobileAuthResultAsync(
            ApplicationUser user,
            RefreshToken? revokeExistingToken)
        {
            await EnsureDisplayNameAsync(user);
            await EnsureFinancialProfileAsync(user);
            await _subscriptionService.EnsureWelcomePackageAsync(user.Id);

            if (revokeExistingToken != null)
            {
                revokeExistingToken.IsRevoked = true;
            }

            var refreshLifetime = TimeSpan.FromDays(30);
            var refreshValue = GenerateRefreshToken();

            _dbContext.RefreshTokens.Add(new RefreshToken
            {
                Token = refreshValue,
                UserId = user.Id,
                Created = DateTime.UtcNow,
                Expires = DateTime.UtcNow + refreshLifetime
            });

            await _dbContext.SaveChangesAsync();

            return new MobileAuthResultDto
            {
                AccessToken = await GenerateJwtToken(user),
                RefreshToken = refreshValue,
                User = new UserProfileDto
                {
                    Id = user.Id,
                    Email = user.Email ?? string.Empty,
                    DisplayName = user.DisplayName ?? string.Empty
                }
            };
        }

        private async Task<string> GenerateJwtToken(ApplicationUser user)
        {
            var key = Encoding.UTF8.GetBytes(_config["Jwt:Key"]);
            var creds = new SigningCredentials(new SymmetricSecurityKey(key)
            { KeyId = _config["Jwt:KeyId"] }, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id),
                new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
                new(JwtRegisteredClaimNames.Name, user.DisplayName ?? string.Empty),
                new(ClaimTypes.Name, user.DisplayName ?? string.Empty),
                new("displayName", user.DisplayName ?? string.Empty),
                new("lifetimeAccess", user.HasLifetimeAccess.ToString())
            };

            UserSubscription? activeSubscription = null;
            SubscriptionPlan? activePlan = null;

            if (user.CurrentSubscriptionId.HasValue)
            {
                activeSubscription = await _dbContext.UserSubscriptions
                    .Include(s => s.Plan)
                    .FirstOrDefaultAsync(s => s.Id == user.CurrentSubscriptionId.Value);

                activePlan = activeSubscription?.Plan;
            }

            if (activeSubscription != null)
            {
                claims.Add(new Claim("subscriptionId", activeSubscription.Id.ToString()));
                claims.Add(new Claim("subscriptionStatus", activeSubscription.Status.ToString()));
                if (activeSubscription.EndDate.HasValue)
                {
                    claims.Add(new Claim("subscriptionEndsAt", activeSubscription.EndDate.Value.ToString("o")));
                }
            }

            var canHideCaptions = activeSubscription != null || user.HasLifetimeAccess;
            claims.Add(new Claim("subscriptionCanHideCaptions", canHideCaptions.ToString()));

            if (activePlan != null)
            {
                claims.Add(new Claim("subscriptionPlanCode", activePlan.Code));
                claims.Add(new Claim("subscriptionPlanPeriod", activePlan.BillingPeriod.ToString()));
                claims.Add(new Claim("subscriptionUnlimited", activePlan.IsUnlimitedRecognitions.ToString()));
                if (activePlan.MaxRecognitionsPerDay.HasValue)
                {
                    claims.Add(new Claim("subscriptionDailyLimit", activePlan.MaxRecognitionsPerDay.Value.ToString()));
                }
            }

            var now = DateTime.UtcNow;
            var featureFlags = await _dbContext.UserFeatureFlags
                .Where(f => f.UserId == user.Id && (f.ExpiresAt == null || f.ExpiresAt > now))
                .ToListAsync();

            foreach (var flag in featureFlags)
            {
                claims.Add(new Claim($"feature:{flag.FeatureCode}", flag.Value ?? "true"));
            }

            var roles = await _userManager.GetRolesAsync(user);
            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

            var expireMinutes = _config.GetValue<int?>("Jwt:ExpireMinutes") ?? 60;

            var token = new JwtSecurityToken(
                _config["Jwt:Issuer"],
                _config["Jwt:Audience"],
                claims,
                expires: DateTime.UtcNow.AddMinutes(expireMinutes),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private async Task EnsureDisplayNameAsync(ApplicationUser user)
        {
            if (!string.IsNullOrWhiteSpace(user.DisplayName))
            {
                return;
            }

            user.DisplayName = GenerateDefaultDisplayName();
            await _userManager.UpdateAsync(user);
        }

        private async Task EnsureFinancialProfileAsync(ApplicationUser user)
        {
            var hasWallet = await _dbContext.UserWallets.AnyAsync(w => w.UserId == user.Id);
            if (!hasWallet)
            {
                _dbContext.UserWallets.Add(new UserWallet
                {
                    UserId = user.Id,
                    Balance = 0,
                    Currency = "RUB",
                    UpdatedAt = DateTime.UtcNow
                });
                await _dbContext.SaveChangesAsync();
            }
        }

        private static string GenerateDefaultDisplayName()
        {
            var number = RandomNumberGenerator.GetInt32(0, 1_000_000);
            return $"User{number:000000}";
        }

        private static bool TryValidateMobileRedirectUri(string? redirectUri, out string normalizedRedirectUri)
        {
            normalizedRedirectUri = string.Empty;

            if (string.IsNullOrWhiteSpace(redirectUri))
            {
                return false;
            }

            if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri))
            {
                return false;
            }

            var isAllowedCustomScheme =
                string.Equals(uri.Scheme, "youscriptor", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Scheme, "scriptor", StringComparison.OrdinalIgnoreCase);

            var isAllowedLoopback =
                (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) &&
                (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase));

            if (!isAllowedCustomScheme && !isAllowedLoopback)
            {
                return false;
            }

            normalizedRedirectUri = uri.ToString();
            return true;
        }

        private static string AppendQueryParameter(string url, string key, string value)
        {
            return QueryHelpers.AddQueryString(url, key, value);
        }

        public class UpdateProfileRequest
        {
            [Required]
            [StringLength(100)]
            public string DisplayName { get; set; } = string.Empty;
        }

        private sealed class ExternalLoginCompletionResult
        {
            public bool Success { get; private init; }

            public string? Error { get; private init; }

            public ApplicationUser? User { get; private init; }

            public static ExternalLoginCompletionResult Completed(ApplicationUser user)
            {
                return new ExternalLoginCompletionResult
                {
                    Success = true,
                    User = user
                };
            }

            public static ExternalLoginCompletionResult Failed(string error)
            {
                return new ExternalLoginCompletionResult
                {
                    Success = false,
                    Error = error
                };
            }
        }
    }
}
