using System;
using System.Collections.Generic;
using System.Linq;
using YoutubeExplode.Videos.ClosedCaptions;



public class CaptionSegment
{
    public string Text { get; set; }
    public int WordCount { get; set; }
    public double Time { get; set; } // В секундах
    public double PauseBeforeNext { get; set; } // В секундах
}

public class CaptionProcessor
{
    public List<string> SegmentCaptions(
        List<ClosedCaption> captions,
        int maxWordsInSegment = 50,
        int windowSize = 5,
        double pauseThreshold = 1.0)
    {
        if (captions == null || captions.Count == 0)
            return new List<string>();

        // Шаг 1: Преобразование списка ClosedCaption в список CaptionSegment
        var segments = new List<CaptionSegment>();



        for (int i = 0; i < captions.Count; i++)
        {
            var current = captions[i];
          /*  int wordCount = CountWords(current.Text);

            double pause = 0.0;
            if (i < captions.Count - 1)
            {
                var next = captions[i + 1];
                var endCurrent = current.Offset;// + current.Parts.Max(x => x.Offset);

                if (current.Parts.Any())
                    endCurrent += current.Parts.Max(x => x.Offset);
                else
                {
                    if (segments.Any())
                    {
                        segments[segments.Count - 1].PauseBeforeNext += current.Duration.TotalSeconds;
                        //  segments[segments.Count - 1].Text += ' '+current.Text;
                    }
                    continue;
                }

                var pauseSpan = next.Offset - endCurrent;
                pause = pauseSpan.TotalSeconds;
            }*/


            if (current.Parts.Count == 0 && current.Text.Split(' ').Length >2 )
            {
                segments.Add(new CaptionSegment
                {
                    Text = current.Text,
                    WordCount = 1,
                    Time = current.Offset.TotalSeconds + current.Duration.TotalSeconds,
                    PauseBeforeNext = 0
                });
            }
            else
            foreach (var d in current.Parts)
                segments.Add(new CaptionSegment
                {
                    Text = d.Text,
                    WordCount = 1,
                    Time = current.Offset.TotalSeconds + d.Offset.TotalSeconds,
                    PauseBeforeNext = 0
                });
        }

        for (int i = 0; i < segments.Count-1; i++)
        {
            segments[i].PauseBeforeNext = segments[i + 1].Time - segments[i].Time;
            /*
            if (segments[i].PauseBeforeNext > 1.5)
                segments[i].Text += '|';
            else
            if (segments[i].PauseBeforeNext > 0.7)
                segments[i].Text += '.';
            */
        }







            /*
            try
            {

                for (int i = 0; i < captions.Count; i++)
                {
                    var current = captions[i];
                    int wordCount = CountWords(current.Text);

                    double pause = 0.0;
                    if (i < captions.Count - 1)
                    {
                        var next = captions[i + 1];
                        var endCurrent = current.Offset;// + current.Parts.Max(x => x.Offset);

                        if (current.Parts.Any())
                            endCurrent += current.Parts.Max(x => x.Offset);
                        else
                        {
                            if (segments.Any())
                            {
                                segments[segments.Count - 1].PauseBeforeNext += current.Duration.TotalSeconds;
                              //  segments[segments.Count - 1].Text += ' '+current.Text;
                            }
                            continue;
                        }

                        var pauseSpan = next.Offset - endCurrent;
                        pause = pauseSpan.TotalSeconds;
                    }

                    segments.Add(new CaptionSegment
                    {
                        Text = current.Text,
                        WordCount = wordCount,
                        PauseBeforeNext = pause
                    });
                }
            }
            catch (Exception ex)
            {

            }
            */

            // Шаг 2: Сегментация с использованием окна вокруг maxWordsInSegment
            var result = new List<string>();
        int start = 0;

     //   File.WriteAllText("c:/amd/11.txt", string.Join(' ', segments.Select(x => x.Text)));

        double maxPause = 0;

        int maxPauseIndex = 0;

        while (start < segments.Count)
        {
           

            int end = start;
            int currentWordCount = 0;

            if (segments.Count - start < maxWordsInSegment)
                end = segments.Count;

            else
            {

                var searchStart = start + maxWordsInSegment - windowSize;
                var searchEnd = start + maxWordsInSegment + windowSize;

                for (int i = searchStart; i < searchEnd && i < segments.Count; i++)
                {
                    if (segments[i].PauseBeforeNext > maxPause)
                    {
                        maxPause = segments[i].PauseBeforeNext;
                        maxPauseIndex = i;
                    }
                }

                end = maxPauseIndex;

            }

            // Собираем текст сегмента
            var segmentText = string.Join(" ", segments.Skip(start).Take(end - start+1).Select(s => s.Text));
            result.Add(segmentText.Replace("  "," "));

            maxPause = 0;

            // Обновляем стартовую позицию
            start = end;
        }
        /*
        File.WriteAllText("c:/amd/112.txt", string.Join(' ', result));*/
        return result;
    }

    private int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        // Простый подсчет слов, разделенных пробелами
        return text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
