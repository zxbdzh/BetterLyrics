using BetterLyrics.Sdk.Models.Display;

namespace BetterLyrics.Sdk.Interfaces.Plugins
{
    public interface ILyricsDisplay
    {
        Task OnSongChangedAsync(DisplaySongInfo songInfo);

        Task OnLyricsChangedAsync(IReadOnlyList<DisplayLyricsLine>? lines);

        Task OnLineChangedAsync(int lineIndex, DisplayLyricsLine? currentLine, DisplayLyricsLine? nextLine);

        Task OnPositionUpdateAsync(double positionMs);

        Task OnPlaybackStateChangedAsync(bool isPlaying);

        int DesiredPositionUpdateIntervalMs { get; }
    }
}
