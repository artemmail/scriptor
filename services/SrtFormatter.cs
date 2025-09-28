using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace YandexSpeech.services
{
    internal static class SrtFormatter
    {
        internal sealed class SrtEntry
        {
            public TimeSpan Start { get; init; }
            public TimeSpan? End { get; init; }
            public string Text { get; init; } = string.Empty;
        }

        public static string Build(IEnumerable<SrtEntry> entries)
        {
            if (entries == null)
            {
                return string.Empty;
            }

            var ordered = entries
                .Where(entry => entry != null)
                .Select(entry => new SrtEntry
                {
                    Start = entry.Start < TimeSpan.Zero ? TimeSpan.Zero : entry.Start,
                    End = entry.End,
                    Text = (entry.Text ?? string.Empty).Replace("\r", string.Empty).Trim()
                })
                .OrderBy(entry => entry.Start)
                .ToList();

            if (ordered.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            var index = 1;

            for (var i = 0; i < ordered.Count; i++)
            {
                var current = ordered[i];
                if (string.IsNullOrWhiteSpace(current.Text) || current.Text == "\\n" || current.Text == "\n")
                {
                    continue;
                }

                var start = current.Start;
                var end = current.End ?? FindNextStart(ordered, i) - TimeSpan.FromMilliseconds(1);
                if (end <= start)
                {
                    end = start + TimeSpan.FromMilliseconds(500);
                }

                builder.AppendLine(index.ToString());
                builder.AppendLine($"{FormatTimestamp(start)} --> {FormatTimestamp(end)}");
                builder.AppendLine(current.Text);
                builder.AppendLine();

                index++;
            }

            return builder.ToString();
        }

        private static TimeSpan FindNextStart(IReadOnlyList<SrtEntry> entries, int index)
        {
            for (var i = index + 1; i < entries.Count; i++)
            {
                var text = entries[i].Text;
                if (!string.IsNullOrWhiteSpace(text) && text != "\\n" && text != "\n")
                {
                    return entries[i].Start;
                }
            }

            return entries[index].Start + TimeSpan.FromSeconds(2);
        }

        private static string FormatTimestamp(TimeSpan time)
        {
            return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00},{time.Milliseconds:000}";
        }
    }
}
