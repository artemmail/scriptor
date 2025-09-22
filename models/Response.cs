using Newtonsoft.Json;

namespace YandexSpeech.models;

public class Response
{
    [JsonProperty("@type")]
    public string type { get; set; }
    public List<Chunk> chunks { get; set; }
}
