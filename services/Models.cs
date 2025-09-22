using System.Globalization;
using Newtonsoft.Json;

namespace YandexSpeech.services
{
    public class Tokens
    {
        [JsonProperty("accessKey")]
        public AccessKey AccessKey { get; set; }

        [JsonProperty("secret")]
        public string Secret { get; set; }
    }

    public class AccessKey
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("serviceAccountId")]
        public string ServiceAccountId { get; set; }

        [JsonProperty("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("keyId")]
        public string KeyId { get; set; }
    }

    public class GPTResult
    {
        public string text { get; set; }
    }

    public class GptTask
    {
        public string prompt { get; set; }
        public string text { get; set; }

        public GptTask(string prompt, string text)
        {
            this.prompt = prompt;
            this.text = text;
        }
    }

    public class RecognizeResult
    {
        public string id { get; set; }
        public DateTime createdAt { get; set; }
        public string createdBy { get; set; }
        public DateTime modifiedAt { get; set; }

        public bool done { get; set; } // Свойство для поля "done"
        public RecognizeResponse response { get; set; } // Поле "response"
    }

    public class RecognizeResponse
    {
        public string type { get; set; } // Поле "@type"
        public List<Chunk> chunks { get; set; } // Поле "chunks"
    }

    public class Chunk
    {
        public List<Alternative> alternatives { get; set; } // Поле "alternatives"
        public int channelTag { get; set; } // Поле "channelTag"
    }

    public class Alternative
    {
        public List<Word> words { get; set; } // Поле "words"
        public string text { get; set; } // Поле "text"
        public float confidence { get; set; } // Поле "confidence"
    }

    public class Word
    {
        public string _startTime;
        public string _endTime;

        public string startTime
        {
            get => _startTime;
            set
            {
                _startTime = value;
                startTimeSpan = ParseTimeToTimeSpan(_startTime);
            }
        }

        public string endTime
        {
            get => _endTime;
            set
            {
                _endTime = value;
                endTimeSpan = ParseTimeToTimeSpan(_endTime);
            }
        }

        public string word { get; set; } // Поле "word" в JSON
        public float confidence { get; set; } // Поле "confidence"

        // Свойства для системного времени
        public TimeSpan startTimeSpan { get;  set; }
        public TimeSpan endTimeSpan { get;  set; }

        // Метод для преобразования времени из строки формата "1.480s" в TimeSpan
        private TimeSpan ParseTimeToTimeSpan(string time)
        {
            if (time.EndsWith("s"))
            {
                time = time.TrimEnd('s');
                if (
                    double.TryParse(
                        time,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double seconds
                    )
                )
                {
                    return TimeSpan.FromSeconds(seconds);
                }
            }
            throw new FormatException($"Invalid time format: {time}");
        }
    }
}
