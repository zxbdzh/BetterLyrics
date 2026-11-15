// 2025/6/23 by Zhe Fang

using System.Text;

namespace BetterLyrics.Core.Models.Lyrics;

public class LyricsLine : BaseLyrics
{
    public new int StartIndex = 0;

    public void RebuildPrimaryChars()
    {
        PrimaryChars.Clear();

        // 音节在对象初始化后才填入，必须等解析和时长补全结束后再生成字符时间轴。
        foreach (var syllable in PrimarySyllables.OrderBy(item => item.StartIndex))
        {
            var startIndex = Math.Clamp(syllable.StartIndex, 0, PrimaryText.Length);
            var length = Math.Min(syllable.Length, PrimaryText.Length - startIndex);
            if (length == 0) continue;

            var duration = Math.Max(syllable.DurationMs, length);
            var offset = 0;
            foreach (var rune in PrimaryText.Substring(startIndex, length).EnumerateRunes())
            {
                var startMs = syllable.StartMs + (int)((long)duration * offset / length);
                var endMs = syllable.StartMs +
                            (int)((long)duration * (offset + rune.Utf16SequenceLength) / length);
                PrimaryChars.Add(new BaseLyrics
                {
                    StartIndex = startIndex + offset,
                    StartMs = startMs,
                    EndMs = Math.Max(startMs + 1, endMs),
                    Text = rune.ToString()
                });
                offset += rune.Utf16SequenceLength;
            }
        }
    }

    public List<BaseLyrics> PrimarySyllables { get; set; } = [];
    public List<BaseLyrics> SecondarySyllables { get; set; } = [];
    public List<BaseLyrics> TertiarySyllables { get; set; } = [];

    public List<BaseLyrics> PrimaryChars { get; } = [];
    public List<BaseLyrics> SecondaryChars { get; private set; } = [];
    public List<BaseLyrics> TertiaryChars { get; private set; } = [];

    public string PrimaryText { get; set; } = "";
    public string SecondaryText { get; set; } = "";
    public string TertiaryText { get; set; } = "";

    public new string Text => PrimaryText;

    public bool IsPrimaryHasRealSyllableInfo { get; set; } = false;

    public string AgentId { get; set; } = "";
}
