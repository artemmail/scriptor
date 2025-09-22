namespace YandexSpeech.models;

public class RecognizeResult
{
    public bool done { get; set; }
    public Response response { get; set; }
    public string id { get; set; }
    public DateTime createdAt { get; set; }
    public string createdBy { get; set; }
    public DateTime modifiedAt { get; set; }
}
