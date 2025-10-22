using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace YandexSpeech.services
{
    public class BillDetails
    {
        [JsonExtensionData]
        public IDictionary<string, JToken>? Data { get; set; }
    }

    public class OperationDetails
    {
        [JsonProperty("operation_id")]
        public string? OperationId { get; set; }

        [JsonProperty("title")]
        public string? Title { get; set; }

        [JsonProperty("amount"), JsonConverter(typeof(NullableDecimalConverter))]
        public decimal? Amount { get; set; }

        [JsonProperty("datetime")]
        public DateTime? DateTime { get; set; }

        [JsonProperty("status")]
        public string? Status { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken>? AdditionalData { get; set; }
    }

    public class OperationHistory
    {
        [JsonProperty("operation_id")]
        public string? OperationId { get; set; }

        [JsonProperty("title")]
        public string? Title { get; set; }

        [JsonProperty("amount"), JsonConverter(typeof(NullableDecimalConverter))]
        public decimal? Amount { get; set; }

        [JsonProperty("datetime")]
        public DateTime? DateTime { get; set; }

        [JsonProperty("status")]
        public string? Status { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken>? AdditionalData { get; set; }
    }

    public class OperationHistoryResponse
    {
        [JsonProperty("operations")]
        public List<OperationHistory>? Operations { get; set; }
    }

    /// <summary>
    /// Converter that handles decimal values represented as strings or numbers.
    /// </summary>
    internal sealed class NullableDecimalConverter : JsonConverter<decimal?>
    {
        public override decimal? ReadJson(JsonReader reader, Type objectType, decimal? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }

            if (reader.TokenType == JsonToken.Float || reader.TokenType == JsonToken.Integer)
            {
                return Convert.ToDecimal(reader.Value);
            }

            if (reader.TokenType == JsonToken.String)
            {
                var text = reader.Value?.ToString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var result))
                {
                    return result;
                }
            }

            throw new JsonSerializationException($"Unable to convert value '{reader.Value}' to decimal.");
        }

        public override void WriteJson(JsonWriter writer, decimal? value, JsonSerializer serializer)
        {
            if (value.HasValue)
            {
                writer.WriteValue(value.Value);
            }
            else
            {
                writer.WriteNull();
            }
        }
    }
}
