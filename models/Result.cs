namespace YandexSpeech.models;

public class Result
{
    public List<Alternative1> alternatives { get; set; }
    public Usage usage { get; set; }
    public string modelVersion { get; set; }
}
