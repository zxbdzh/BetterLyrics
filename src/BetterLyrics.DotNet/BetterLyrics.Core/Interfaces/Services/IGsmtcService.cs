// 2025/6/23 by Zhe Fang

using BetterLyrics.Core.Enums;
using BetterLyrics.Core.Models;
using BetterLyrics.Core.Models.Domain;
using BetterLyrics.Core.Models.Entities;
using BetterLyrics.Core.Models.Lyrics;
using BetterLyrics.Core.Models.Http;
using BetterLyrics.Core.Models.Settings;
using System.ComponentModel;

namespace BetterLyrics.Core.Interfaces.Services;

/// <summary>
/// Interface for GlobalSystemMediaTransportControlsSession Service
/// </summary>
public interface IGsmtcService : INotifyPropertyChanged
{
    MediaSourceProviderInfo? CurrentMediaSourceProviderInfo { get; }
    bool IsExternalSourceActive { get; }

    bool IsScrobbled { get; }
    TimeSpan ScrobbledDuration { get; }
    TimeSpan TargetScrobbledDuration { get; }

    bool CurrentIsPlaying { get; }
    SongInfo CurrentSongInfo { get; }
    TimeSpan CurrentPosition { get; }
    LyricsData? CurrentLyricsData { get; }

    byte[]? AlbumArtBytes { get; }

    LyricsCacheItem? CurrentLyricsSearchResult { get; }
    OhMyGoMusicStatus? CurrentOhMyGoMusicStatus { get; }
    Task PlayAsync();
    Task PauseAsync();
    Task StopAsync();
    Task PreviousAsync();
    Task NextAsync();
    Task ChangePositionAsync(double seconds);
    Task ChangeLyricsLineAsync(int index);
    Task WriteCurrentOhMyGoMusicLyricsAsync(OhMyGoMusicStatus? expected, SongInfo song, OhMyGoMusicLyrics lyrics, CancellationToken token = default);

    void UpdateLyrics();

    Task<NowPlayingPalette> CalculateAlbumArtThemeColorsAsync(LyricsWindowStatus lyricsWindowStatus,
        AppColor backdropAccentColor, CancellationToken token = default);

    Task<List<AppColor>> GetAlbumArtAccentColorsAsync(PaletteGeneratorType paletteGeneratorType, bool isDark,
        CancellationToken token = default);
}
