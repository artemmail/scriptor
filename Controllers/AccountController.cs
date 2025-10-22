using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using YandexSpeech.models.DB;
using YandexSpeech.models.DTO;
using YandexSpeech.models.DTO.Account;
using Microsoft.AspNetCore.Http;
using YandexSpeech.Extensions;
using YandexSpeech.services.Google;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.WebUtilities;

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
        private readonly IGoogleTokenService _googleTokenService;

        public AccountController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IConfiguration config,
            MyDbContext dbContext,
            IGoogleTokenService googleTokenService)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _config = config;
            _dbContext = dbContext;
            _googleTokenService = googleTokenService;
        }

        // ---------- EXTERNAL SIGN-IN ----------

        [AllowAnonymous]
        [HttpGet("login")]
        public IActionResult Login([FromQuery] string? returnUrl = null, [FromQuery] bool calendar = false)
        {
            var model = new ExternalLoginViewModel
            {
                ReturnUrl = returnUrl,
                RequestCalendarAccess = calendar
            };

            return View("Login", model);
        }

        [AllowAnonymous]
        [HttpGet("externallogin")]
        public IActionResult ExternalLogin(
            [FromQuery] string provider,
            [FromQuery] string? returnUrl = null,
            [FromQuery] bool calendar = false,
            [FromQuery] string? prompt = null,
            [FromQuery] bool calendarDeclined = false)
        {
            if (string.IsNullOrWhiteSpace(provider))
            {
                return BadRequest("Provider is required.");
            }

            return InitiateExternalLogin(provider, returnUrl, calendar, prompt, calendarDeclined);
        }

        [AllowAnonymous]
        [HttpGet("signin-google")]
        public IActionResult SignInWithGoogle(string? returnUrl = null, bool calendar = false, string? prompt = null, bool calendarDeclined = false)
            => InitiateExternalLogin("Google", returnUrl, calendar, prompt, calendarDeclined);

        [AllowAnonymous]
        [HttpGet("signin-yandex")]
        public IActionResult SignInWithYandex(string? returnUrl = null)
            => InitiateExternalLogin("Yandex", returnUrl);

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

            var isGoogleProvider = string.Equals(info.LoginProvider, "Google", StringComparison.OrdinalIgnoreCase);
            var calendarRequested = false;
            var calendarDeclined = false;

            if (isGoogleProvider && info.AuthenticationProperties != null &&
                info.AuthenticationProperties.Items.TryGetValue(ServiceCollectionExtensions.CalendarAccessPropertyName, out var calendarValue) &&
                bool.TryParse(calendarValue, out var calendarFlag))
            {
                calendarRequested = calendarFlag;
            }

            if (isGoogleProvider && info.AuthenticationProperties != null &&
                info.AuthenticationProperties.Items.TryGetValue(ServiceCollectionExtensions.CalendarDeclinedPropertyName, out var calendarDeclinedValue) &&
                bool.TryParse(calendarDeclinedValue, out var calendarDeclinedFlag))
            {
                calendarDeclined = calendarDeclinedFlag;
            }

            if (isGoogleProvider)
            {
                await _signInManager.UpdateExternalAuthenticationTokensAsync(info);
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
                var email = info.Principal.FindFirstValue(ClaimTypes.Email)
                            ?? info.Principal.FindFirstValue("urn:yandex:email");
                if (string.IsNullOrEmpty(email))
                {
                    Console.WriteLine("ExternalLoginCallback: EmailNotFound");
                    return Redirect($"{GetAngularRedirectUrl()}?error=EmailNotFound");
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

            if (isGoogleProvider)
            {
                if (calendarDeclined)
                {
                    await _googleTokenService.RecordCalendarDeclinedAsync(
                        user,
                        HttpContext.RequestAborted);
                }

                var updateResult = await _googleTokenService.EnsureAccessTokenAsync(
                    user,
                    calendarRequested,
                    info.AuthenticationTokens,
                    HttpContext.RequestAborted);

                if (calendarRequested && !updateResult.Succeeded)
                {
                    Console.WriteLine($"Failed to update Google Calendar tokens: {updateResult.ErrorMessage}");
                }
            }

            await EnsureDisplayNameAsync(user);
            await EnsureFinancialProfileAsync(user);
            await EnsureFinancialProfileAsync(user);

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

        [HttpGet("google-calendar/status")]
        public async Task<ActionResult<GoogleCalendarStatusDto>> GetGoogleCalendarStatus()
        {
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

            var logins = await _userManager.GetLoginsAsync(user);
            var isGoogleAccount = logins.Any(l =>
                string.Equals(l.LoginProvider, "Google", StringComparison.OrdinalIgnoreCase));

            var token = await _dbContext.UserGoogleTokens
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    t => t.UserId == user.Id && t.TokenType == GoogleTokenTypes.Calendar,
                    HttpContext.RequestAborted);

            var status = BuildGoogleCalendarStatus(user, token, isGoogleAccount);
            return Ok(status);
        }

        [HttpPost("google-calendar/disconnect")]
        public async Task<IActionResult> DisconnectGoogleCalendar()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Unauthorized();
            }

            var result = await _googleTokenService.RevokeAsync(user, HttpContext.RequestAborted);
            if (!result.Succeeded)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, new
                {
                    success = false,
                    updated = false,
                    error = result.ErrorMessage ?? "Не удалось отключить интеграцию Google Calendar."
                });
            }

            return Ok(new
            {
                success = true,
                updated = result.Updated,
                message = result.Updated
                    ? "Доступ к Google Calendar отключён."
                    : "Интеграция Google Calendar уже была отключена."
            });
        }

        [HttpGet("google-calendar/connect")]
        public async Task<IActionResult> ConnectGoogleCalendar([FromQuery] string? returnUrl = null)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return Unauthorized();
            }

            var redirectUrl = Url.Action(nameof(GoogleCalendarCallback), "Account", new { returnUrl });
            var properties = _signInManager.ConfigureExternalAuthenticationProperties(
                GoogleDefaults.AuthenticationScheme,
                redirectUrl,
                user.Id);

            properties.Items[ServiceCollectionExtensions.CalendarAccessPropertyName] = bool.TrueString;
            properties.Items[ServiceCollectionExtensions.PromptPropertyName] = "consent";

            return Challenge(properties, GoogleDefaults.AuthenticationScheme);
        }

        [AllowAnonymous]
        [HttpGet("google-calendar/callback")]
        public async Task<IActionResult> GoogleCalendarCallback(
            string? returnUrl = null,
            string? error = null,
            string? error_description = null)
        {
            string status;
            string? message = null;
            string? errorMessage = null;

            if (!string.IsNullOrEmpty(error))
            {
                status = "error";
                errorMessage = error_description ?? error;
                await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
                return Redirect(BuildGoogleCalendarRedirectUrl(returnUrl, status, message, errorMessage));
            }

            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                status = "error";
                errorMessage = "Не удалось получить данные от Google.";
                await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
                return Redirect(BuildGoogleCalendarRedirectUrl(returnUrl, status, message, errorMessage));
            }

            var calendarRequested = info.AuthenticationProperties != null
                && info.AuthenticationProperties.Items.TryGetValue(ServiceCollectionExtensions.CalendarAccessPropertyName, out var calendarValue)
                && bool.TryParse(calendarValue, out var calendarFlag)
                && calendarFlag;

            if (!calendarRequested)
            {
                status = "error";
                errorMessage = "Запрос доступа к календарю не был подтверждён.";
                await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
                return Redirect(BuildGoogleCalendarRedirectUrl(returnUrl, status, message, errorMessage));
            }

            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId) && info.AuthenticationProperties != null)
            {
                info.AuthenticationProperties.Items.TryGetValue("XsrfId", out userId);
            }

            if (string.IsNullOrEmpty(userId))
            {
                status = "error";
                errorMessage = "Не удалось определить пользователя.";
                await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
                return Redirect(BuildGoogleCalendarRedirectUrl(returnUrl, status, message, errorMessage));
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                status = "error";
                errorMessage = "Пользователь не найден.";
                await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
                return Redirect(BuildGoogleCalendarRedirectUrl(returnUrl, status, message, errorMessage));
            }

            await _signInManager.UpdateExternalAuthenticationTokensAsync(info);

            var updateResult = await _googleTokenService.EnsureAccessTokenAsync(
                user,
                consentGranted: true,
                info.AuthenticationTokens,
                HttpContext.RequestAborted);

            if (!updateResult.Succeeded)
            {
                status = "error";
                errorMessage = updateResult.ErrorMessage ?? "Не удалось подключить Google Calendar.";
            }
            else if (updateResult.Updated)
            {
                status = "connected";
                message = "Google Calendar подключён.";
            }
            else
            {
                status = "unchanged";
                message = "Права доступа уже были предоставлены ранее.";
            }

            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

            return Redirect(BuildGoogleCalendarRedirectUrl(returnUrl, status, message, errorMessage));
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

            await EnsureDisplayNameAsync(user);

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

        private GoogleCalendarStatusDto BuildGoogleCalendarStatus(
            ApplicationUser user,
            UserGoogleToken? token,
            bool isGoogleAccount)
        {
            var revokedAfterConsent = token != null
                && token.RevokedAt.HasValue
                && (!token.ConsentGrantedAt.HasValue || token.RevokedAt.Value >= token.ConsentGrantedAt.Value);

            var isConnected = token != null
                && token.ConsentGrantedAt.HasValue
                && !revokedAfterConsent
                && !string.IsNullOrWhiteSpace(token.AccessToken);

            return new GoogleCalendarStatusDto
            {
                IsGoogleAccount = isGoogleAccount,
                IsConnected = isConnected,
                HasRefreshToken = !string.IsNullOrWhiteSpace(token?.RefreshToken),
                ConsentGrantedAt = token?.ConsentGrantedAt,
                ConsentDeclinedAt = token?.ConsentDeclinedAt,
                TokensRevokedAt = token?.RevokedAt,
                AccessTokenUpdatedAt = token?.AccessTokenUpdatedAt,
                AccessTokenExpiresAt = token?.AccessTokenExpiresAt,
                RefreshTokenExpiresAt = token?.RefreshTokenExpiresAt
            };
        }

        private string BuildGoogleCalendarRedirectUrl(
            string? returnUrl,
            string status,
            string? message,
            string? error)
        {
            var sanitized = SanitizeReturnUrl(returnUrl);
            var query = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["calendarStatus"] = status
            };

            if (!string.IsNullOrWhiteSpace(message))
            {
                query["calendarMessage"] = message;
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                query["calendarError"] = error;
            }

            var target = QueryHelpers.AddQueryString(sanitized.BasePathWithQuery, query);
            return string.Concat(target, sanitized.Fragment);
        }

        private (string BasePathWithQuery, string Fragment) SanitizeReturnUrl(string? returnUrl)
        {
            const string fallback = "/profile";
            if (string.IsNullOrWhiteSpace(returnUrl))
            {
                return (fallback, string.Empty);
            }

            var fragment = string.Empty;
            var pathPart = returnUrl;

            var hashIndex = returnUrl.IndexOf('#');
            if (hashIndex >= 0)
            {
                fragment = returnUrl.Substring(hashIndex);
                pathPart = returnUrl.Substring(0, hashIndex);
            }

            if (Uri.TryCreate(pathPart, UriKind.Absolute, out var absolute))
            {
                if (!string.Equals(absolute.Host, Request.Host.Host, StringComparison.OrdinalIgnoreCase))
                {
                    return (fallback, string.Empty);
                }

                var normalizedPath = absolute.PathAndQuery;
                return (string.IsNullOrEmpty(normalizedPath) ? fallback : normalizedPath, fragment);
            }

            if (!Uri.TryCreate(pathPart, UriKind.Relative, out _) || !pathPart.StartsWith('/'))
            {
                return (fallback, string.Empty);
            }

            return (pathPart, fragment);
        }

        private IActionResult InitiateExternalLogin(
            string provider,
            string? returnUrl,
            bool calendarAccess = false,
            string? prompt = null,
            bool calendarDeclined = false)
        {
            var redirectUrl = Url.Action(nameof(ExternalLoginCallback), "Account", new { returnUrl });
            var props = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);

            if (string.Equals(provider, "Google", StringComparison.OrdinalIgnoreCase))
            {
                props.Items[ServiceCollectionExtensions.CalendarAccessPropertyName] = calendarAccess.ToString();
                if (calendarDeclined)
                {
                    props.Items[ServiceCollectionExtensions.CalendarDeclinedPropertyName] = bool.TrueString;
                }
                var promptValue = !string.IsNullOrWhiteSpace(prompt) ? prompt : (calendarAccess ? "consent" : string.Empty);

                if (!string.IsNullOrWhiteSpace(promptValue))
                {
                    props.Items[ServiceCollectionExtensions.PromptPropertyName] = promptValue;
                }
            }

            return Challenge(props, provider);
        }

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

            var token = new JwtSecurityToken(
                _config["Jwt:Issuer"],
                _config["Jwt:Audience"],
                claims,
                expires: DateTime.UtcNow.AddMinutes(60),
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

        public class UpdateProfileRequest
        {
            [Required]
            [StringLength(100)]
            public string DisplayName { get; set; } = string.Empty;
        }
    }
}
