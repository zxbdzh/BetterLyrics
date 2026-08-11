// 2025/6/23 by Zhe Fang

namespace BetterLyrics.Core.Models.Lyrics
{
    public class LyricsLine : BaseLyrics
    {
        public List<BaseLyrics> PrimarySyllables { get; set; } = [];
        public List<BaseLyrics> SecondarySyllables { get; set; } = [];
        public List<BaseLyrics> TertiarySyllables { get; set; } = [];

        public List<BaseLyrics> PrimaryChars { get; private set; } = [];
        public List<BaseLyrics> SecondaryChars { get; private set; } = [];
        public List<BaseLyrics> TertiaryChars { get; private set; } = [];

        public string PrimaryText { get; set; } = "";
        public string SecondaryText { get; set; } = "";
        public string TertiaryText { get; set; } = "";

        public new string Text => PrimaryText;
        public new int StartIndex = 0;

        public bool IsPrimaryHasRealSyllableInfo { get; set; } = false;

        public bool IsShareable { get; set; } = true;

        public string AgentId { get; set; } = "";

        public void RebuildPrimaryChars()
        {
            PrimaryChars.Clear();

            foreach (var syllable in PrimarySyllables.OrderBy(x => x.StartIndex))
            {
                var startIndex = Math.Clamp(syllable.StartIndex, 0, PrimaryText.Length);
                var length = Math.Min(syllable.Length, PrimaryText.Length - startIndex);
                if (length <= 0) continue;

                var duration = Math.Max(syllable.DurationMs, length);
                for (var offset = 0; offset < length; offset++)
                {
                    var charStartMs = syllable.StartMs + (int)((long)duration * offset / length);
                    var charEndMs = syllable.StartMs + (int)((long)duration * (offset + 1) / length);

                    PrimaryChars.Add(new BaseLyrics
                    {
                        StartIndex = startIndex + offset,
                        StartMs = charStartMs,
                        EndMs = Math.Max(charStartMs + 1, charEndMs),
                        Text = PrimaryText[startIndex + offset].ToString()
                    });
                }
            }
        }

    }
}
