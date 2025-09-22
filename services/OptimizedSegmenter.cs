using System.Text;
using YoutubeExplode.Videos.ClosedCaptions;

namespace opt

{ 
    // Пример (укороченные) классов, которые используются в TextSegmenter
    public class RecognizeResult
    {
        public RecognizeResponse response { get; set; }
    }

    public class RecognizeResponse
    {
        public List<Chunk> chunks { get; set; }
    }

    public class Chunk
    {
        public string channelTag { get; set; }
        public List<Alternative> alternatives { get; set; }
    }

    public class Alternative
    {
        public string text { get; set; }
        public List<Word> words { get; set; }
    }

    public class Word
    {
        public string word { get; set; }
        public float confidence { get; set; }
        public TimeSpan startTimeSpan { get; set; }
        public TimeSpan endTimeSpan { get; set; }
    }

    // ----------------------- НАЧАЛО ОСНОВНОГО КОДА -----------------------

    public class OptimizedSegmenter
    {
        /// <summary>
        /// Функция сегментации субтитров на крупные блоки (с учётом maxWordsInSegment и pauseThreshold).
        /// Параметры и возвращаемое значение идентичны исходной версии.
        /// </summary>
        /// <param name="captions">Список субтитров</param>
        /// <param name="maxWordsInSegment">Максимум слов в одном блоке</param>
        /// <param name="pauseThreshold">Порог паузы (сек) для дополнительного разбиения сегмента</param>
        /// <returns>Список итоговых строк (сегментов)</returns>
        public List<string> SegmentCaptions(
            List<ClosedCaption> captions,
            int maxWordsInSegment = 50,
            double pauseThreshold = 1.0
        )
        {
            if (captions == null || captions.Count == 0)
            {
                Console.WriteLine("Входной список субтитров пуст.");
                return new List<string>();
            }

            Console.WriteLine($"Количество субтитров для обработки: {captions.Count}");

            // Готовим pseudo-"RecognizeResult", но не разбиваем текст на слова
            // — создаём всего один Word на весь субтитр.
            // Количество слов вычисляем отдельно (для логики сегментации).
            var chunks = new List<Chunk>();
            foreach (var c in captions)
            {
                // Подсчитаем слова в самом тексте (чтобы корректно учитывался maxWordsInSegment)
                var wordCount = c.Text
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Length;

                // Генерируем один Word — только ради сохранения start/end для текущего блока
                var singleWord = new Word
                {
                    word = c.Text, // Можно оставить сам текст, либо что-то краткое
                    confidence = 1.0f,
                    startTimeSpan = c.Offset,
                    endTimeSpan = c.Offset + c.Duration
                };

                // Собираем Alternative
                var alt = new Alternative
                {
                    text = c.Text,
                    words = new List<Word> { singleWord }
                };

                // Создаём Chunk и добавляем в список
                chunks.Add(new Chunk
                {
                    // channelTag можно выставить какой-то один (например, "0")
                    channelTag = "0",
                    alternatives = new List<Alternative> { alt }
                });
            }

            var recognizeResult = new RecognizeResult
            {
                response = new RecognizeResponse
                {
                    chunks = chunks
                }
            };

            // Теперь пользуемся тем же TextSegmenter (из вашего кода),
            // но с тем отличием, что real word-count мы вручную проставим
            // при формировании Phrase (см. правку в BuildPhrases).
            var segments = TextSegmenterWithOverride.SplitIntoSegments(
                recognizeResult,
                // фильтруем по "каналу" 0
                channel: 0,
                maxWordsInSegment: maxWordsInSegment,
                pauseThreshold: pauseThreshold,
                separatorPause: 1.0 // Можно оставить как есть или вынести параметром
            );

            if (segments == null || segments.Count == 0)
            {
                Console.WriteLine("Метод сегментации вернул пустой результат.");
                return new List<string>();
            }

            Console.WriteLine($"Количество сегментов после обработки: {segments.Count}");
            return segments;
        }
    }

    /// <summary>
    /// Модифицированный TextSegmenter, чтобы учитывать реальный WordCount,
    /// который мы посчитали выше, а не только alt.words.Count.
    /// </summary>
    public static class TextSegmenterWithOverride
    {
        public static List<string> SplitIntoSegments(
            RecognizeResult result,
            int channel = 1,
            int maxWordsInSegment = 4000,
            double pauseThreshold = 1.0,
            double separatorPause = 1.0
        )
        {
            // 1. Собираем все фразы в один список (только для нужного channel)
            var phrases = BuildPhrases(result, channel);

            // Если фраз нет, возвращаем пустой список
            if (!phrases.Any())
                return new List<string>();

            // Сортируем (на всякий случай) по стартовому времени
            phrases = phrases.OrderBy(p => p.StartTime).ToList();

            // 2. Непосредственно разбиение на блоки
            var segments = new List<List<Phrase>>();
            var currentSegment = new List<Phrase>();
            int currentWordCount = 0;

            foreach (var phrase in phrases)
            {
                currentSegment.Add(phrase);
                currentWordCount += phrase.WordCount;

                if (currentWordCount >= maxWordsInSegment)
                {
                    int splitIndex = FindBestSplitIndex(currentSegment, pauseThreshold);

                    if (splitIndex > 0)
                    {
                        var leftPart = currentSegment.Take(splitIndex).ToList();
                        var rightPart = currentSegment.Skip(splitIndex).ToList();

                        segments.Add(leftPart);
                        currentSegment = rightPart;
                        currentWordCount = currentSegment.Sum(p => p.WordCount);
                    }
                    else
                    {
                        segments.Add(currentSegment);
                        currentSegment = new List<Phrase>();
                        currentWordCount = 0;
                    }
                }
            }

            // Добавляем хвост
            if (currentSegment.Any())
                segments.Add(currentSegment);

            // 3. Превращаем списки фраз в строки
            var resultStrings = new List<string>();
            foreach (var segPhrases in segments)
            {
                var merged = MergePhrasesIntoString(segPhrases, separatorPause);
                if (!string.IsNullOrWhiteSpace(merged))
                    resultStrings.Add(merged);
            }

            return resultStrings;
        }

        /// <summary>
        /// Сборка фраз (Phrase) по нужному "каналу".
        /// ВАЖНО: ниже правка — WordCount берём не из alt.words.Count, 
        /// а вычисляем при создании Phrase через сам alt.text (если нужно).
        /// Но т.к. мы уже заранее разбили текст на слова (wordCount), 
        /// можем сохранить его прямо в alt.text, например в начале строки,
        /// или воспользоваться другим способом. Ниже — упрощённо.
        /// </summary>
        private static List<Phrase> BuildPhrases(RecognizeResult result, int channel)
        {
            var phrases = new List<Phrase>();
            if (result?.response?.chunks == null)
                return phrases;

            // Приводим к строке, чтобы сравнить с channelTag
            string channelStr = channel.ToString();

            var filteredChunks = result.response.chunks
                .Where(c => c.channelTag == channelStr)
                .ToList();

            foreach (var chunk in filteredChunks)
            {
                var alt = chunk.alternatives?.FirstOrDefault();
                if (alt == null) continue;
                if (alt.words == null || alt.words.Count == 0) continue;

                // Подсчёт реального количества слов:
                // Допустим, сам текст = alt.text.
                // Можно сделать Split, но обычно мы это уже сделали выше
                // и как-то сохранили (например, в самом alt.text).
                // Ниже — «ленивый» подсчёт:
                int realWordCount = alt.text
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Length;

                var phrase = new Phrase
                {
                    Text = alt.text,
                    // Начальное время = startTimeSpan единственного (или первого) Word
                    StartTime = alt.words.First().startTimeSpan,
                    // Конечное время = endTimeSpan единственного (или последнего) Word
                    EndTime = alt.words.Last().endTimeSpan,
                    WordCount = realWordCount
                };

                phrases.Add(phrase);
            }

            return phrases;
        }

        private static int FindBestSplitIndex(List<Phrase> phrases, double pauseThreshold)
        {
            int bestSplitIndex = -1;
            double maxPause = 0;

            for (int i = 0; i < phrases.Count - 1; i++)
            {
                var pause = (phrases[i + 1].StartTime - phrases[i].EndTime).TotalSeconds;
                if (pause >= pauseThreshold && pause > maxPause)
                {
                    maxPause = pause;
                    bestSplitIndex = i + 1;
                }
            }

            return bestSplitIndex;
        }

        private static string MergePhrasesIntoString(List<Phrase> phrases, double separatorPause)
        {
            if (phrases.Count == 0) return string.Empty;
            if (phrases.Count == 1) return phrases[0].Text ?? string.Empty;

            var sb = new StringBuilder();
            sb.Append(phrases[0].Text);

            for (int i = 1; i < phrases.Count; i++)
            {
                var previous = phrases[i - 1];
                var current = phrases[i];
                var pause = (current.StartTime - previous.EndTime).TotalSeconds;

                if (pause > separatorPause)
                    sb.Append(" | ");
                else
                    sb.Append(" ");

                sb.Append(current.Text);
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Класс для хранения одной "фразы" целиком (для дальнейшей сегментации).
    /// </summary>
    public class Phrase
    {
        public string Text { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public int WordCount { get; set; }
    }

}