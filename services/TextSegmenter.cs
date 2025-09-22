using System.Text;

namespace YandexSpeech.services
{
    /// <summary>
    /// Класс для удобного хранения одной "фразы" целиком,
    /// а не отдельных слов. Нужен для дальнейшей сегментации.
    /// </summary>
    public class Phrase
    {
        public string Text { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public int WordCount { get; set; }
    }

    public static class TextSegmenter
    {
        /// <summary>
        /// Разбивает результаты распознавания на крупные блоки с "усложнённой" логикой:
        /// 1) Фильтр по каналу.
        /// 2) Сбор всех "фраз" (Alternative.text) с общими таймкодами.
        /// 3) Разбиение на блоки до maxWordsInSegment слов, при этом ищем самую большую паузу внутри, если нужно разбить.
        /// 4) При склеивании фраз в один блок, если пауза между соседними фразами более separatorPause сек, ставим " | ", иначе " ".
        /// </summary>
        /// <param name="result">Результат распознавания речи</param>
        /// <param name="channel">Номер (или метка) канала, по которому фильтруем</param>
        /// <param name="maxWordsInSegment">Максимум слов в одном блоке</param>
        /// <param name="pauseThreshold">Порог паузы (сек.) для "глубокого" разбиения больших блоков</param>
        /// <param name="separatorPause">Порог паузы (сек.) для вставки разделителя " | "</param>
        /// <returns>Список итоговых строк</returns>
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
            var segments = new List<List<Phrase>>(); // каждый элемент - список фраз, входящих в сегмент
            var currentSegment = new List<Phrase>();
            int currentWordCount = 0;

            foreach (var phrase in phrases)
            {
                // Добавляем очередную фразу в текущий "пакет"
                currentSegment.Add(phrase);
                currentWordCount += phrase.WordCount;

                // Если по добавлению этой фразы мы превысили лимит,
                // ищем внутри "currentSegment" место для "оптимального" разрыва
                if (currentWordCount >= maxWordsInSegment)
                {
                    int splitIndex = FindBestSplitIndex(currentSegment, pauseThreshold);

                    if (splitIndex > 0)
                    {
                        // разбиваем currentSegment на 2 части
                        var leftPart = currentSegment.Take(splitIndex).ToList();
                        var rightPart = currentSegment.Skip(splitIndex).ToList();

                        // закрываем левую часть как готовый сегмент
                        segments.Add(leftPart);

                        // в правой части продолжим работу
                        currentSegment = rightPart;
                        currentWordCount = currentSegment.Sum(p => p.WordCount);
                    }
                    else
                    {
                        // Если нет подходящей паузы - добавляем весь currentSegment целиком
                        segments.Add(currentSegment);
                        currentSegment = new List<Phrase>();
                        currentWordCount = 0;
                    }
                }
            }

            // Добавляем финальный сегмент (если что-то осталось)
            if (currentSegment.Any())
            {
                segments.Add(currentSegment);
            }

            // 3. Превращаем списки фраз в готовые строки
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
        /// Собирает список фраз (Phrase) по заданному каналу.
        /// Фраза = вся строка alternative.text,
        /// время = от первого слова до последнего,
        /// WordCount = количество слов.
        /// </summary>
        private static List<Phrase> BuildPhrases(RecognizeResult result, int channel)
        {
            var phrases = new List<Phrase>();

            if (result?.response?.chunks == null)
                return phrases;

            // Фильтруем нужные чанки
            var filteredChunks = result
                .response.chunks.Where(c => c.channelTag == channel) // channelTag - строка, а channel - int?
                .ToList();

            foreach (var chunk in filteredChunks)
            {
                var alt = chunk.alternatives?.FirstOrDefault();
                if (alt == null)
                    continue;

                // Если нет слов - пропустим
                if (alt.words == null || alt.words.Count == 0)
                    continue;

                var phrase = new Phrase
                {
                    Text = alt.text,
                    StartTime = alt.words.First().startTimeSpan,
                    EndTime = alt.words.Last().endTimeSpan,
                    WordCount = alt.words.Count,
                };

                phrases.Add(phrase);
            }

            return phrases;
        }

        /// <summary>
        /// Ищет индекс фразы, с которой нужно "отсекать" текущий список,
        /// чтобы разрыв пришёлся на самую большую паузу (>= pauseThreshold).
        /// Если такой паузы нет, возвращается -1 (т.е. не разрывать).
        /// </summary>
        private static int FindBestSplitIndex(List<Phrase> phrases, double pauseThreshold)
        {
            int bestSplitIndex = -1;
            double maxPause = 0;

            // Идём по парам фраз: (phrases[i], phrases[i+1])
            for (int i = 0; i < phrases.Count - 1; i++)
            {
                var pause = (phrases[i + 1].StartTime - phrases[i].EndTime).TotalSeconds;
                if (pause >= pauseThreshold && pause > maxPause)
                {
                    maxPause = pause;
                    // splitIndex = i+1 => значит, первые (i+1) фраз уйдут в левый блок
                    bestSplitIndex = i + 1;
                }
            }

            return bestSplitIndex;
        }

        /// <summary>
        /// Склеивает фразы в одну строку.
        /// Если пауза между соседними фразами больше separatorPause,
        /// вставляем " | ", иначе - " ".
        /// </summary>
        private static string MergePhrasesIntoString(List<Phrase> phrases, double separatorPause)
        {
            if (phrases.Count == 0)
                return string.Empty;
            if (phrases.Count == 1)
                return phrases[0].Text ?? string.Empty;

            var sb = new StringBuilder();
            sb.Append(phrases[0].Text);

            for (int i = 1; i < phrases.Count; i++)
            {
                var previous = phrases[i - 1];
                var current = phrases[i];
                var pause = (current.StartTime - previous.EndTime).TotalSeconds;

                // Если пауза "свыше" порога — ставим разделитель " | "
                // Иначе — обычный пробел
                if (pause > separatorPause)
                    sb.Append(" | ");
                else
                    sb.Append(" ");

                sb.Append(current.Text);
            }

            return sb.ToString();
        }
    }
}
