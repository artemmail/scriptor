using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using YandexSpeech.Extensions;
using YandexSpeech.models.DB;
using YandexSpeech.models.DTO.Profile;
using YandexSpeech.services.Google;

namespace YandexSpeech.Controllers
{
    [Authorize]
    [Route("profile")]
    public class ProfileController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly MyDbContext _dbContext;
        private readonly IGoogleTokenService _googleTokenService;

        public ProfileController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            MyDbContext dbContext,
            IGoogleTokenService googleTokenService)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _dbContext = dbContext;
            _googleTokenService = googleTokenService;
        }

        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var token = await _dbContext.UserGoogleTokens
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.UserId == user.Id && t.TokenType == GoogleTokenTypes.Calendar, HttpContext.RequestAborted);

            var model = BuildViewModel(user, token);
            return View("Index", model);
        }

        [HttpPost("google-calendar/connect")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConnectGoogleCalendar()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                TempData["ProfileError"] = "Не удалось определить пользователя.";
                return RedirectToAction(nameof(Index));
            }

            var redirectUrl = Url.Action(nameof(GoogleCalendarCallback), "Profile");
            var properties = _signInManager.ConfigureExternalAuthenticationProperties(
                GoogleDefaults.AuthenticationScheme,
                redirectUrl,
                user.Id);

            properties.Items[ServiceCollectionExtensions.CalendarAccessPropertyName] = bool.TrueString;
            properties.Items[ServiceCollectionExtensions.PromptPropertyName] = "consent";
            properties.Items["profile:google-calendar"] = "true";

            return Challenge(properties, GoogleDefaults.AuthenticationScheme);
        }

        [AllowAnonymous]
        [HttpGet("google-calendar/callback")]
        public async Task<IActionResult> GoogleCalendarCallback(string? error = null, string? error_description = null)
        {
            if (!string.IsNullOrEmpty(error))
            {
                TempData["ProfileError"] = $"Google вернул ошибку: {error_description ?? error}.";
                await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
                return RedirectToAction(nameof(Index));
            }

            var info = await _signInManager.GetExternalLoginInfoAsync();
            if (info == null)
            {
                TempData["ProfileError"] = "Не удалось получить данные от Google.";
                await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
                return RedirectToAction(nameof(Index));
            }

            var calendarRequested = info.AuthenticationProperties != null
                && info.AuthenticationProperties.Items.TryGetValue(ServiceCollectionExtensions.CalendarAccessPropertyName, out var calendarValue)
                && bool.TryParse(calendarValue, out var calendarFlag)
                && calendarFlag;

            if (!calendarRequested)
            {
                TempData["ProfileError"] = "Запрос доступа к календарю не был подтверждён.";
                await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
                return RedirectToAction(nameof(Index));
            }

            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrEmpty(userId) && info.AuthenticationProperties != null)
            {
                info.AuthenticationProperties.Items.TryGetValue("XsrfId", out userId);
            }

            if (string.IsNullOrEmpty(userId))
            {
                TempData["ProfileError"] = "Не удалось определить пользователя.";
                await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
                return RedirectToAction(nameof(Index));
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                TempData["ProfileError"] = "Пользователь не найден.";
                await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
                return RedirectToAction(nameof(Index));
            }

            await _signInManager.UpdateExternalAuthenticationTokensAsync(info);

            var updateResult = await _googleTokenService.EnsureAccessTokenAsync(
                user,
                consentGranted: true,
                info.AuthenticationTokens,
                HttpContext.RequestAborted);
            if (!updateResult.Succeeded)
            {
                TempData["ProfileError"] = updateResult.ErrorMessage ?? "Не удалось подключить Google Calendar.";
            }
            else if (updateResult.Updated)
            {
                TempData["ProfileSuccess"] = "Google Calendar подключён.";
            }
            else
            {
                TempData["ProfileInfo"] = "Права доступа уже были предоставлены ранее.";
            }

            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

            return RedirectToAction(nameof(Index));
        }

        [HttpPost("google-calendar/disconnect")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DisconnectGoogleCalendar()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                TempData["ProfileError"] = "Не удалось определить пользователя.";
                return RedirectToAction(nameof(Index));
            }

            var updateResult = await _googleTokenService.RevokeAsync(user, HttpContext.RequestAborted);
            if (!updateResult.Succeeded)
            {
                TempData["ProfileError"] = updateResult.ErrorMessage ?? "Не удалось отключить интеграцию Google Calendar.";
            }
            else if (updateResult.Updated)
            {
                TempData["ProfileSuccess"] = "Google Calendar отключён.";
            }
            else
            {
                TempData["ProfileInfo"] = "Интеграция Google Calendar уже была отключена.";
            }

            return RedirectToAction(nameof(Index));
        }

        private static ProfileIndexViewModel BuildViewModel(ApplicationUser user, UserGoogleToken? token)
        {
            var revokedAfterConsent = token != null
                && token.RevokedAt.HasValue
                && (!token.ConsentGrantedAt.HasValue || token.RevokedAt.Value >= token.ConsentGrantedAt.Value);

            var isConnected = token != null
                && token.ConsentGrantedAt.HasValue
                && !revokedAfterConsent
                && !string.IsNullOrWhiteSpace(token.AccessToken);

            return new ProfileIndexViewModel
            {
                Email = user.Email ?? string.Empty,
                DisplayName = user.DisplayName ?? string.Empty,
                GoogleCalendar = new GoogleCalendarStatusViewModel
                {
                    IsConnected = isConnected,
                    HasRefreshToken = !string.IsNullOrWhiteSpace(token?.RefreshToken),
                    ConsentAt = token?.ConsentGrantedAt,
                    AccessTokenUpdatedAt = token?.AccessTokenUpdatedAt,
                    AccessTokenExpiresAt = token?.AccessTokenExpiresAt,
                    RefreshTokenExpiresAt = token?.RefreshTokenExpiresAt,
                    TokensRevokedAt = token?.RevokedAt
                }
            };
        }
    }
}
