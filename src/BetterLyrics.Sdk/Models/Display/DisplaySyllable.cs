namespace BetterLyrics.Sdk.Models.Display
{
    public record DisplaySyllable(int StartMs, int? EndMs, string Text, int StartIndex)
    {
        public int DurationMs => Math.Max((EndMs ?? 0) - StartMs, 0);
    }
}
