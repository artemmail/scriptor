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
    public const int MaxBlockWords = 1200;
    public const int MaxBlockCharacters = 12000;
    public const int MaxContextCharacters = 4000;

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

        var wordLimit = maxWordsInSegment > 0 ? Math.Min(maxWordsInSegment, MaxBlockWords) : MaxBlockWords;
        // Keep source indices for audio timestamps, even when one caption spans many blocks.
        var words = new List<(string Text, int Source, double Pause)>();
        for (var i = 0; i < segments.Count; i++)
        {
            foreach (var word in (segments[i].Text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                for (var offset = 0; offset < word.Length;)
                {
                    var length = Math.Min(MaxBlockCharacters, word.Length - offset);
                    if (offset + length < word.Length && char.IsHighSurrogate(word[offset + length - 1]))
                        length--;
                    words.Add((word.Substring(offset, length), i, 0));
                    offset += length;
                }
            }
            if (words.Count > 0 && words[^1].Source == i && i + 1 < segments.Count)
            {
                var last = words[^1];
                words[^1] = (last.Text, i, Math.Max(0, segments[i + 1].Time - (segments[i].EndTime ?? segments[i].Time)));
            }
        }

        for (var start = 0; start < words.Count;)
        {
            var end = start;
            var characters = 0;
            while (end < words.Count && end - start < wordLimit)
            {
                var added = words[end].Text.Length + (end > start ? 1 : 0);
                if (characters + added > MaxBlockCharacters)
                    break;
                characters += added;
                end++;
            }

            // Prefer a pause near the end, without ever exceeding either hard limit.
            if (end < words.Count)
            {
                var searchStart = Math.Max(start + (end - start) / 2, end - Math.Max(0, windowSize));
                var bestPause = pauseThreshold;
                var split = end;
                for (var i = searchStart; i < end; i++)
                {
                    if (words[i].Pause > bestPause)
                    {
                        bestPause = words[i].Pause;
                        split = i + 1;
                    }
                }
                end = split;
            }

            result.Add(new CaptionSegmentBlock
            {
                StartIndex = words[start].Source,
                EndIndex = words[end - 1].Source,
                Text = string.Join(" ", words.Skip(start).Take(end - start).Select(w => w.Text))
            });
            start = end;
        }

        return result;
    }

    private static List<CaptionSegment> BuildSegmentsFromCaptions(List<ClosedCaption> captions)
    {
        var segments = new List<CaptionSegment>();

        for (int i = 0; i < captions.Count; i++)
        {
            var current = captions[i];

            if (current.Parts.Count == 0)
            {
                segments.Add(new CaptionSegment
                {
                    Text = current.Text,
                    WordCount = 1,
                    Time = current.Offset.TotalSeconds,
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
