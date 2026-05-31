using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YandexSpeech.services
{
    public static class PaymentOperationPayloadTypes
    {
        public const string Wallet = "wallet";
        public const string Subscription = "subscription";
    }

    public sealed class PaymentOperationPayload
    {
        public string Type { get; set; } = string.Empty;

        public Guid? PlanId { get; set; }

        public string? Comment { get; set; }
    }

    public static class PaymentOperationPayloadSerializer
    {
        private static readonly JsonSerializerOptions ReadOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly JsonSerializerOptions WriteOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static string Serialize(PaymentOperationPayload payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            return JsonSerializer.Serialize(payload, WriteOptions);
        }

        public static PaymentOperationPayload? Deserialize(string? payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<PaymentOperationPayload>(payload, ReadOptions);
            }
            catch
            {
                return null;
            }
        }
    }
}
