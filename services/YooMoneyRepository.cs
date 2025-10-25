using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using YandexSpeech.services.Interface;

namespace YandexSpeech.services
{
    public class YooMoneyOptions
    {
        public string? ClientId { get; set; }

        public string? AccessToken { get; set; }

        public string? RedirectUri { get; set; }

        public string? Receiver { get; set; }

        public string QuickpayForm { get; set; } = "shop";

        public string PaymentType { get; set; } = "AC";

        public string? SuccessUrl { get; set; }

        public string? FailUrl { get; set; }

        public string? NotificationSecret { get; set; }
    }

    public class YooMoneyRepository : IYooMoneyRepository
    {
        private static readonly Uri BaseUri = new("https://yoomoney.ru/");

        private readonly HttpClient httpClient;
        private readonly YooMoneyOptions options;

        public YooMoneyRepository(HttpClient httpClient, IOptions<YooMoneyOptions> optionsAccessor)
        {
            this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            ArgumentNullException.ThrowIfNull(optionsAccessor);

            options = optionsAccessor.Value ?? throw new InvalidOperationException("YooMoney options are not configured.");
            var clientId = EnsureConfigured(options.ClientId, nameof(options.ClientId));
            var accessToken = EnsureConfigured(options.AccessToken, nameof(options.AccessToken));
            var redirectUri = EnsureConfigured(options.RedirectUri, nameof(options.RedirectUri));

            // ensure http client configured once
            if (httpClient.BaseAddress == null)
            {
                httpClient.BaseAddress = BaseUri;
            }

            // store validated values
            options.ClientId = clientId;
            options.AccessToken = accessToken;
            options.RedirectUri = redirectUri;
        }

        public async Task<OperationDetails?> GetOperationDetailsAsync(string operationId, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(operationId);

            var responseJson = await PostAsync("api/operation-details",
                new[] { new KeyValuePair<string, string>("operation_id", operationId) },
                includeToken: true,
                cancellationToken).ConfigureAwait(false);

            return JsonConvert.DeserializeObject<OperationDetails>(responseJson);
        }

        public async Task<IReadOnlyList<OperationHistory>?> GetOperationHistoryAsync(int from, int count, CancellationToken cancellationToken = default)
        {
            if (from < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(from));
            }

            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            var responseJson = await PostAsync("api/operation-history",
                new[]
                {
                    new KeyValuePair<string, string>("records", count.ToString()),
                    new KeyValuePair<string, string>("start_record", from.ToString())
                },
                includeToken: true,
                cancellationToken).ConfigureAwait(false);

            var response = JsonConvert.DeserializeObject<OperationHistoryResponse>(responseJson);
            return response?.Operations;
        }

        public async Task<BillDetails?> GetBillDetailsAsync(string billId, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(billId);

            var responseJson = await PostAsync(
                "api/bill-details",
                new[] { new KeyValuePair<string, string>("bill_id", billId) },
                includeToken: true,
                cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return null;
            }

            return JsonConvert.DeserializeObject<BillDetails>(responseJson);
        }

        public Task<string> AuthorizeAsync(CancellationToken cancellationToken = default)
        {
            var payload = new[]
            {
                new KeyValuePair<string, string>("client_id", options.ClientId!),
                new KeyValuePair<string, string>("response_type", "code"),
                new KeyValuePair<string, string>("redirect_uri", options.RedirectUri!),
                new KeyValuePair<string, string>("scope", "account-info operation-history operation-details"),
            };

            return PostAsync("oauth/authorize", payload, includeToken: false, cancellationToken);
        }

        public Task<string> ExchangeTokenAsync(string code, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(code);

            var payload = new[]
            {
                new KeyValuePair<string, string>("code", code),
                new KeyValuePair<string, string>("client_id", options.ClientId!),
                new KeyValuePair<string, string>("grant_type", "authorization_code"),
                new KeyValuePair<string, string>("redirect_uri", options.RedirectUri!),
            };

            return PostAsync("oauth/authorize", payload, includeToken: false, cancellationToken);
        }

        private async Task<string> PostAsync(string endpoint, IEnumerable<KeyValuePair<string, string>> payload, bool includeToken, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new FormUrlEncodedContent(payload)
            };

            if (includeToken)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.AccessToken);
            }

            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }

        private static string EnsureConfigured(string? value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Configuration value 'YooMoney:{name}' is missing or empty.");
            }

            return value;
        }
    }
}
