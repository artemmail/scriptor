namespace YandexSpeech.models;

public class Specification
{
    public string languageCode { get; set; }
    public string model { get; set; }
    public bool profanityFilter { get; set; }
    public bool literature_text { get; set; }
    public bool rawResults { get; set; }
}
