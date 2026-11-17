using BetterLyrics.Core.Enums;
using BetterLyrics.Core.Models.Entities;
using BetterLyrics.Core.Models.Http;
using BetterLyrics.Core.Models.Lyrics;
using System.Globalization;
using System.Text;

namespace BetterLyrics.Core.Implementations.Services.GsmtcService;

public static class OhMyGoMusicLyricsAdapter
{
    public static LyricsCacheItem ToCacheItem(OhMyGoMusicLyrics source)
    {
        var raw = string.IsNullOrWhiteSpace(source.Lrc) ? BuildLrc(source.Lines, false) : source.Lrc;
        var translation = string.IsNullOrWhiteSpace(source.Translation) ? BuildLrc(source.Lines, true) : source.Translation;
        return new LyricsCacheItem
        {
            Provider = LyricsProvider.OhMyGoMusic,
            Raw = raw,
            Translation = translation
        };
    }

    public static void ApplyWordTimes(LyricsData parsed, OhMyGoMusicLyrics source)
    {
        foreach (var sourceLine in source.Lines)
        {
            if (sourceLine.Start < 0)
            {
                continue;
            }

            var text = string.IsNullOrEmpty(sourceLine.Text)
                ? string.Concat(sourceLine.Words.Select(word => word.Text))
                : sourceLine.Text;
            // LRC 通常只到厘秒，允许最多 9 ms 舍入差，但词时间仍使用源毫秒值。
            var targetLine = parsed.LyricsLines.FirstOrDefault(line =>
                line.PrimaryText == text && Math.Abs((long)line.StartMs - sourceLine.Start) < 10);
            if (targetLine == null)
            {
                continue;
            }
            if (sourceLine.End > sourceLine.Start) targetLine.EndMs = sourceLine.End;
            if (sourceLine.Words.Count == 0) continue;

            var syllables = new List<BaseLyrics>(sourceLine.Words.Count);
            var startIndex = 0;
            foreach (var word in sourceLine.Words)
            {
                if (word.Start < 0 || word.End <= word.Start || word.End > int.MaxValue)
                {
                    syllables.Clear();
                    break;
                }
                syllables.Add(new BaseLyrics
                {
                    StartMs = (int)word.Start,
                    EndMs = (int)word.End,
                    StartIndex = startIndex,
                    Text = word.Text
                });
                startIndex += word.Text.Length;
            }

            if (syllables.Count == 0 || string.Concat(syllables.Select(word => word.Text)) != targetLine.PrimaryText)
            {
                continue;
            }

            targetLine.PrimarySyllables = syllables;
            targetLine.IsPrimaryHasRealSyllableInfo = true;
            targetLine.RebuildPrimaryChars();
        }
    }

    private static string BuildLrc(List<OhMyGoMusicLyricLine> lines, bool translation)
    {
        var result = new StringBuilder();
        foreach (var line in lines)
        {
            if (line.Start < 0) continue;
            var text = translation
                ? line.Translation
                : string.IsNullOrEmpty(line.Text)
                    ? string.Concat(line.Words.Select(word => word.Text))
                    : line.Text;
            if (string.IsNullOrWhiteSpace(text)) continue;
            var totalSeconds = line.Start / 1000;
            result.AppendFormat(
                CultureInfo.InvariantCulture,
                "[{0:00}:{1:00}.{2:000}]{3}\n",
                totalSeconds / 60,
                totalSeconds % 60,
                line.Start % 1000,
                text);
        }
        return result.ToString();
    }
}
