using BetterLyrics.Core.Extensions;
using BetterLyrics.Core.Helpers;
using BetterLyrics.Core.Interfaces.Services;
using BetterLyrics.Core.Messages.Playback;
using BetterLyrics.Core.Models.Entities;
using BetterLyrics.Core.Models.Http;
using BetterLyrics.Core.Models.Lyrics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using LyricsContentParser = BetterLyrics.Core.Helpers.Lyrics.ContentParser.LyricsContentParser;

namespace BetterLyrics.Core.Implementations.Services.GsmtcService;

public abstract partial class GsmtcServiceBase : IGsmtcService
{
    private readonly Debouncer _lyricsDebouncer = new();
    private readonly Debouncer _lyricsPresentationDebouncer = new();
    private (LyricsCacheItem Result, OhMyGoMusicLyrics Source)? _ohMyGoMusicLyricsSnapshot;

    /// <summary>
    /// 标记本次 <see cref="CurrentLyricsData"/> 变更是否只是译文（翻译 / 音译）的显示变化
    /// </summary>
    private bool _isPresentationUpdate;

    [ObservableProperty]
    public partial LyricsData? CurrentLyricsData { get; private set; }

    partial void OnCurrentLyricsDataChanged(LyricsData? value) =>
        SendMessage(new CurrentLyricsDataChangedMessage(value, _isPresentationUpdate));

    [ObservableProperty] public partial LyricsCacheItem? CurrentLyricsSearchResult { get; private set; }

    public void UpdateLyrics()
    {
        _ = _lyricsDebouncer.RunAsync(async token => await RefreshLyricsAsync(token));
    }

    public void UpdateLyricsPresentation()
    {
        var searchResult = CurrentLyricsSearchResult;
        if (searchResult == null)
        {
            // 没有缓存结果（例如尚未成功获取歌词）时退回到完整刷新
            UpdateLyrics();
            return;
        }

        var sourceLyrics = _ohMyGoMusicLyricsSnapshot is { } snapshot &&
                           ReferenceEquals(snapshot.Result, searchResult)
            ? snapshot.Source
            : null;
        _ = _lyricsPresentationDebouncer.RunAsync(async token =>
        {
            var lyricsParser = new LyricsContentParser();
            var lyricsData = await Task.Run(async () => await lyricsParser.ParseAsync(searchResult, token), token);

            token.ThrowIfCancellationRequested();

            // 解析期间歌曲或歌词源已经变化，本次结果作废
            if (!ReferenceEquals(CurrentLyricsSearchResult, searchResult)) return;

            // 显示开关只重排现有歌词，不能丢失来源单独提供的逐字时间。
            if (sourceLyrics != null) OhMyGoMusicLyricsAdapter.ApplyWordTimes(lyricsData, sourceLyrics);
            _isPresentationUpdate = true;
            try
            {
                CurrentLyricsData = lyricsData;
            }
            finally
            {
                _isPresentationUpdate = false;
            }
        });
    }

    private async Task RefreshLyricsAsync(CancellationToken token)
    {
        _ohMyGoMusicLyricsSnapshot = null;
        if (CurrentSongInfo != SongInfoExtensions.Placeholder)
        {
            var maxRetries = _settingsService.AppSettings.GeneralSettings.MaxAutoRetryCount;
            CurrentLyricsSearchResult = null;

            if (PlayerIdHelper.IsOhMyGoMusic(CurrentSongInfo.PlayerId))
            {
                var sourceLyrics = await TryGetOhMyGoMusicLyricsAsync(token);
                if (sourceLyrics != null)
                {
                    var cacheItem = OhMyGoMusicLyricsAdapter.ToCacheItem(sourceLyrics);
                    if (!string.IsNullOrWhiteSpace(cacheItem.Raw))
                    {
                        cacheItem.CopyFromSongInfo(CurrentSongInfo);
                        CurrentLyricsSearchResult = cacheItem;
                        var parser = new LyricsContentParser();
                        var lyricsData = await Task.Run(async () => await parser.ParseAsync(cacheItem, token), token);
                        token.ThrowIfCancellationRequested();
                        OhMyGoMusicLyricsAdapter.ApplyWordTimes(lyricsData, sourceLyrics);
                        _ohMyGoMusicLyricsSnapshot = (cacheItem, sourceLyrics);
                        CurrentLyricsData = lyricsData;
                        return;
                    }
                }
            }

            for (var attempt = 1; attempt <= maxRetries; attempt++)
            {
                CurrentLyricsData = LyricsData.GetLoadingPlaceholder(attempt, maxRetries);

                try
                {
                    CurrentLyricsSearchResult = await Task.Run(async () => await _lyrcsSearchService.SearchSmartlyAsync(
                        CurrentSongInfo, CurrentMediaSourceProviderInfo?.LyricsSearchType, token), token);

                    break;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Lyrics search attempt {Attempt}/{MaxRetries} failed for {Title} - {Artist}.",
                        attempt, maxRetries, CurrentSongInfo.Title, CurrentSongInfo.Artist);

                    if (attempt == maxRetries) break;

                    await Task.Delay((attempt * 2 - 1) * 1000, token);
                }
            }

            if (CurrentLyricsSearchResult != null)
            {
                var lyricsParser = new LyricsContentParser();

                CurrentLyricsData = await Task.Run(async () => await lyricsParser.ParseAsync(CurrentLyricsSearchResult, token), token);
            }
        }
        else
        {
            CurrentLyricsSearchResult = null;
        }

        if (CurrentLyricsSearchResult == null) CurrentLyricsData = LyricsDataExtensions.NotFoundPlaceholder;
    }

    private async Task<OhMyGoMusicLyrics?> TryGetOhMyGoMusicLyricsAsync(CancellationToken token)
    {
        var serverAddress = _settingsService.AppSettings.GeneralSettings.OhMyGoMusicServer;
        try
        {
            var status = await OhMyGoMusicClient.Shared.GetStatusAsync(serverAddress, token);
            if (status == null || status.TrackId == 0) return null;

            var lyrics = await OhMyGoMusicClient.Shared.GetLyricsAsync(serverAddress, status.TrackId, token);
            return lyrics?.TrackId == status.TrackId ? lyrics : null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not retrieve Oh MyGo Music lyrics; using the configured lyric search providers.");
            return null;
        }
    }
}
