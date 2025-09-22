namespace YandexSpeech.models;

public class Alternative
{
    public List<Word> words { get; set; }
    public string text { get; set; }
    public int confidence { get; set; }
}
