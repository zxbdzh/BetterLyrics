using BetterLyrics.Core.Enums;
using BetterLyrics.Core.Models.Entities;
using BetterLyrics.Core.Models.Http;
using BetterLyrics.Core.Models.Lyrics;
using System.Globalization;
using System.Text;

namespace BetterLyrics.Core.Implementations.Services.GsmtcService;

public static class OhMyGoMusicLyricsAdapter
{
    public static OhMyGoMusicLyrics ToWriteModel(LyricsCacheItem source, IReadOnlyList<LyricsData>? parsedTracks = null)
    {
        var result = new OhMyGoMusicLyrics
        {
            Lrc = source.Raw ?? string.Empty,
            Translation = source.Translation ?? string.Empty
        };
        var tracks = parsedTracks ?? source.LyricsDataArr;
        var original = tracks?.FirstOrDefault(data =>
            data.TrackType == LyricsTrackType.Original && !data.IsPlaceholder);
        if (original == null) return result;
        var translation = tracks?.FirstOrDefault(data => data.TrackType == LyricsTrackType.Translation);

        foreach (var line in original.LyricsLines)
        {
            if (line.StartMs < 0 || string.IsNullOrWhiteSpace(line.PrimaryText)) continue;
            var target = new OhMyGoMusicLyricLine
            {
                Start = line.StartMs,
                End = line.EndMs is > 0 ? line.EndMs.Value : 0,
                Text = line.PrimaryText,
                Translation = translation?.LyricsLines.FirstOrDefault(candidate => Math.Abs(candidate.StartMs - line.StartMs) < 50)?.PrimaryText
                    ?? line.SecondaryText
            };
            if (line.IsPrimaryHasRealSyllableInfo)
            {
                var words = line.PrimarySyllables
                    .Where(word => word.StartMs >= line.StartMs && word.EndMs > word.StartMs && !string.IsNullOrEmpty(word.Text))
                    .Select(word => new OhMyGoMusicLyricWord
                    {
                        Start = word.StartMs,
                        End = word.EndMs!.Value,
                        Text = word.Text
                    })
                    .ToList();
                if (words.Count > 0 && words.All(word => target.End == 0 || word.End <= target.End) &&
                    string.Concat(words.Select(word => word.Text)) == line.PrimaryText)
                    target.Words = words;
            }
            result.Lines.Add(target);
        }
        // 外部来源可能是 QRC/TTML；写给 MyGo 的文本统一为 LRC，逐字时间另外无损携带。
        result.Lrc = BuildLrc(result.Lines, false);
        result.Translation = BuildLrc(result.Lines, true);
        return result;
    }

    public static LyricsCacheItem ToCacheItem(OhMyGoMusicLyrics source)
    {
        var raw = string.IsNullOrWhiteSpace(source.Lrc) ? BuildLrc(source.Lines, false) : source.Lrc;
        var translation = string.IsNullOrWhiteSpace(source.Translation) ? BuildLrc(source.Lines, true) : source.Translation;
        return new LyricsCacheItem
        {
            Provider = LyricsProvider.OhMyGoMusic,
            Raw = raw,
            Translation = translation,
            TimedLyrics = source
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
