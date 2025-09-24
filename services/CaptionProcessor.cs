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
    public double? EndTime { get; set; }
}

public class CaptionSegmentBlock
{
    public int StartIndex { get; set; }
    public int EndIndex { get; set; }
    public string Text { get; set; }
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

        var segments = BuildSegmentsFromCaptions(captions);
        return SegmentCaptionSegments(segments, maxWordsInSegment, windowSize, pauseThreshold);
    }

    public List<string> SegmentCaptionSegments(
        List<CaptionSegment> segments,
        int maxWordsInSegment = 50,
        int windowSize = 5,
        double pauseThreshold = 1.0)
    {
        return SegmentCaptionSegmentsDetailed(segments, maxWordsInSegment, windowSize, pauseThreshold)
            .Select(block => block.Text)
            .ToList();
    }

    public List<CaptionSegmentBlock> SegmentCaptionSegmentsDetailed(
        List<CaptionSegment> segments,
        int maxWordsInSegment = 50,
        int windowSize = 5,
        double pauseThreshold = 1.0)
    {
        var result = new List<CaptionSegmentBlock>();

        if (segments == null || segments.Count == 0)
            return result;

        for (int i = 0; i < segments.Count - 1; i++)
        {
            segments[i].PauseBeforeNext = segments[i + 1].Time - segments[i].Time;
        }

        int start = 0;
        while (start < segments.Count)
        {
            int end;

            if (segments.Count - start <= maxWordsInSegment)
            {
                end = segments.Count - 1;
            }
            else
            {
                var searchStart = Math.Max(start + maxWordsInSegment - windowSize, start);
                var searchEnd = Math.Min(start + maxWordsInSegment + windowSize, segments.Count - 1);

                double maxPause = double.MinValue;
                int maxPauseIndex = searchStart;

                for (int i = searchStart; i <= searchEnd; i++)
                {
                    if (segments[i].PauseBeforeNext > maxPause)
                    {
                        maxPause = segments[i].PauseBeforeNext;
                        maxPauseIndex = i;
                    }
                }

                end = Math.Max(maxPauseIndex, start);
            }

            var count = end - start + 1;
            if (count <= 0)
            {
                end = start;
                count = 1;
            }

            var text = string.Join(" ", segments.Skip(start).Take(count).Select(s => s.Text))
                .Replace("  ", " ")
                .Trim();

            result.Add(new CaptionSegmentBlock
            {
                StartIndex = start,
                EndIndex = end,
                Text = text
            });

            start = end + 1;
        }

        return result;
    }

    private static List<CaptionSegment> BuildSegmentsFromCaptions(List<ClosedCaption> captions)
    {
        var segments = new List<CaptionSegment>();

        for (int i = 0; i < captions.Count; i++)
        {
            var current = captions[i];

            if (current.Parts.Count == 0 && current.Text.Split(' ').Length > 2)
            {
                segments.Add(new CaptionSegment
                {
                    Text = current.Text,
                    WordCount = 1,
                    Time = current.Offset.TotalSeconds + current.Duration.TotalSeconds,
                    EndTime = current.Offset.TotalSeconds + current.Duration.TotalSeconds,
                    PauseBeforeNext = 0
                });
            }
            else
            {
                foreach (var part in current.Parts)
                {
                    segments.Add(new CaptionSegment
                    {
                        Text = part.Text,
                        WordCount = 1,
                        Time = current.Offset.TotalSeconds + part.Offset.TotalSeconds,
                        EndTime = current.Offset.TotalSeconds + part.Offset.TotalSeconds,
                        PauseBeforeNext = 0
                    });
                }
            }
        }

        return segments;
    }

    private int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        return text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
