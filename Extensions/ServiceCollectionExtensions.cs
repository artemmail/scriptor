using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace YandexSpeech.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public const string CalendarAccessPropertyName = "google:calendar_access";
        public const string CalendarDeclinedPropertyName = "google:calendar_declined";
        public const string PromptPropertyName = "google:prompt";

        public static AuthenticationBuilder AddGoogleOAuthConfigurations(
            this AuthenticationBuilder builder,
            IConfiguration configuration,
            Action<GoogleOptions>? configure = null)
        {
            var googleSection = configuration.GetSection("Authentication:Google");
            var clientId = googleSection["ClientId"];
            var clientSecret = googleSection["ClientSecret"];

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                return builder;
            }

            var baseScopes = googleSection.GetSection("Scopes").Get<string[]>() ?? Array.Empty<string>();
            if (baseScopes.Length == 0)
            {
                baseScopes = new[] { "openid", "profile", "email" };
            }

            var calendarSection = configuration.GetSection("Authentication:GoogleCalendar");
            var calendarScopes = calendarSection.GetSection("Scopes").Get<string[]>() ?? Array.Empty<string>();
            var calendarPrompt = calendarSection["Prompt"];
            var calendarAccessType = calendarSection["AccessType"];

            builder.AddGoogle(options =>
            {
                options.ClientId = clientId;
                options.ClientSecret = clientSecret;
                options.SaveTokens = true;

                options.Scope.Clear();
                foreach (var scope in baseScopes)
                {
                    if (!string.IsNullOrWhiteSpace(scope))
                    {
                        options.Scope.Add(scope);
                    }
                }

                options.Events ??= new OAuthEvents();

                configure?.Invoke(options);

                var previousOnRedirect = options.Events.OnRedirectToAuthorizationEndpoint;
                options.Events.OnRedirectToAuthorizationEndpoint = context =>
                {
                    var scopeSet = new HashSet<string>(baseScopes, StringComparer.OrdinalIgnoreCase);

                    var hasCalendarRequest = context.Properties.Items.TryGetValue(CalendarAccessPropertyName, out var calendarValue)
                        && bool.TryParse(calendarValue, out var calendarRequested)
                        && calendarRequested;

                    var queryUpdates = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

                    if (hasCalendarRequest)
                    {
                        foreach (var scope in calendarScopes)
                        {
                            if (!string.IsNullOrWhiteSpace(scope))
                            {
                                scopeSet.Add(scope);
                            }
                        }

                        var promptValue = !string.IsNullOrWhiteSpace(calendarPrompt) ? calendarPrompt : null;
                        if (context.Properties.Items.TryGetValue(PromptPropertyName, out var promptOverride)
                            && !string.IsNullOrWhiteSpace(promptOverride))
                        {
                            promptValue = promptOverride;
                        }

                        if (!string.IsNullOrWhiteSpace(promptValue))
                        {
                            queryUpdates["prompt"] = promptValue;
                        }

                        var accessType = !string.IsNullOrWhiteSpace(calendarAccessType) ? calendarAccessType : "offline";
                        queryUpdates["access_type"] = accessType;
                        queryUpdates["include_granted_scopes"] = "true";
                    }
                    else if (context.Properties.Items.TryGetValue(PromptPropertyName, out var prompt)
                        && !string.IsNullOrWhiteSpace(prompt))
                    {
                        queryUpdates["prompt"] = prompt;
                    }

                    queryUpdates["scope"] = string.Join(" ", scopeSet);

                    ApplyRedirectQueryParameters(context, queryUpdates);

                    if (previousOnRedirect != null)
                    {
                        return previousOnRedirect(context);
                    }

                    context.Response.Redirect(context.RedirectUri);
                    return Task.CompletedTask;
                };
            });

            return builder;
        }

        private static void ApplyRedirectQueryParameters(
            RedirectContext<OAuthOptions> context,
            IDictionary<string, string?> updates)
        {
            if (updates.Count == 0)
            {
                return;
            }

            var uriBuilder = new UriBuilder(context.RedirectUri);
            var existingQuery = uriBuilder.Query;
            var queryToParse = string.IsNullOrEmpty(existingQuery)
                ? string.Empty
                : existingQuery.StartsWith("?") ? existingQuery : $"?{existingQuery}";
            var parsedQuery = QueryHelpers.ParseQuery(queryToParse);

            var queryBuilder = new QueryBuilder();

            foreach (var pair in parsedQuery)
            {
                if (updates.ContainsKey(pair.Key))
                {
                    continue;
                }

                foreach (var value in pair.Value)
                {
                    queryBuilder.Add(pair.Key, value);
                }
            }

            foreach (var update in updates)
            {
                if (!string.IsNullOrEmpty(update.Value))
                {
                    queryBuilder.Add(update.Key, update.Value);
                }
            }

            var queryString = queryBuilder.ToQueryString().Value;
            uriBuilder.Query = string.IsNullOrEmpty(queryString) ? string.Empty : queryString.TrimStart('?');
            context.RedirectUri = uriBuilder.Uri.ToString();
        }
    }
}
