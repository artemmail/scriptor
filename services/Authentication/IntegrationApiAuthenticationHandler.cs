using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YandexSpeech.services.Options;

namespace YandexSpeech.services.Authentication
{
    public sealed class IntegrationApiAuthenticationDefaults
    {
        public const string AuthenticationScheme = "IntegrationApiToken";
        public const string HeaderName = "X-Integration-Token";
    }

    public sealed class IntegrationApiAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private readonly IOptionsMonitor<TelegramIntegrationOptions> _integrationOptions;

        public IntegrationApiAuthenticationHandler(
            IOptionsMonitor<TelegramIntegrationOptions> integrationOptions,
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock)
            : base(options, logger, encoder, clock)
        {
            _integrationOptions = integrationOptions;
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var configuredToken = _integrationOptions.CurrentValue.IntegrationApiToken;
            if (string.IsNullOrWhiteSpace(configuredToken))
            {
                return Task.FromResult(AuthenticateResult.Fail("Integration API token is not configured."));
            }

            if (!Request.Headers.TryGetValue(IntegrationApiAuthenticationDefaults.HeaderName, out var provided))
            {
                return Task.FromResult(AuthenticateResult.Fail("Integration API token header is missing."));
            }

            foreach (var candidate in provided)
            {
                if (string.Equals(candidate, configuredToken, System.StringComparison.Ordinal))
                {
                    var claims = new[]
                    {
                        new Claim(ClaimTypes.Name, "TelegramIntegration"),
                        new Claim(ClaimTypes.Role, "Integration")
                    };
                    var identity = new ClaimsIdentity(claims, Scheme.Name);
                    var principal = new ClaimsPrincipal(identity);
                    var ticket = new AuthenticationTicket(principal, Scheme.Name);
                    return Task.FromResult(AuthenticateResult.Success(ticket));
                }
            }

            return Task.FromResult(AuthenticateResult.Fail("Invalid integration API token."));
        }
    }
}
