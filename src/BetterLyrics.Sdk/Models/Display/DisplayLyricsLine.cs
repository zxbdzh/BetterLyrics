namespace BetterLyrics.Sdk.Models.Display
{
    public class DisplayLyricsLine
    {
        public string PrimaryText { get; set; } = "";
        public string SecondaryText { get; set; } = "";
        public string TertiaryText { get; set; } = "";

        public int StartMs { get; set; }
        public int? EndMs { get; set; }
        public int DurationMs => Math.Max((EndMs ?? 0) - StartMs, 0);

        public List<DisplaySyllable> PrimarySyllables { get; set; } = [];
        public bool HasSyllableTimingData { get; set; }
    }
}
