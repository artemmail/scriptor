namespace YandexSpeech.models;

public class RecognizeTask
{
    public RecognizeTask(string file)
    {
        config = new Config()
        {
            specification = new Specification()
            {
                languageCode = "ru-RU",
                model = "hqa",
                profanityFilter = true,
                literature_text = true,
                rawResults = true,
            },
        };
        audio = new Audio() { uri = $"https://storage.yandexcloud.net/ruticker/{file}" };
    }

    public Config config { get; set; }
    public Audio audio { get; set; }
}
