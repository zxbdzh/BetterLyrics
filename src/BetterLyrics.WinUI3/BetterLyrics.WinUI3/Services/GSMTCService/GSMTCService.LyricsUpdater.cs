using BetterLyrics.Core.Extensions;
using BetterLyrics.Core.Constants;
using BetterLyrics.Core.Helpers;
using BetterLyrics.Core.Implementations.Services;
using BetterLyrics.Core.Models.Entities;
using BetterLyrics.Core.Models.Lyrics;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using LyricsContentParser = BetterLyrics.Core.Helpers.Lyrics.ContentParser.LyricsContentParser;

namespace BetterLyrics.WinUI3.Services.GSMTCService
{
    public partial class GSMTCService : IGSMTCService
    {
        private Debouncer _lyricsDebouncer = new();
        private readonly object _podcastLyricsRefreshLock = new();
        private CancellationTokenSource _podcastLyricsRefreshCts = new();
        private bool _podcastLyricsRefreshPending;
        private bool _podcastLyricsRefreshRunning;

        [ObservableProperty]
        [NotifyPropertyChangedRecipients]
        public partial LyricsData? CurrentLyricsData { get; private set; }

        [ObservableProperty]
        public partial LyricsCacheItem? CurrentLyricsSearchResult { get; private set; }

        private async Task RefreshLyricsAsync(CancellationToken token)
        {
            if (PlayerIdHelper.IsLXMusic(CurrentSongInfo.PlayerId) && _lxMediaKindSignal != null)
            {
                await Task.WhenAny(_lxMediaKindSignal.Task, Task.Delay(TimeSpan.FromSeconds(1), token));
                token.ThrowIfCancellationRequested();
            }

            if (IsPodcast)
            {
                CurrentLyricsSearchResult = null;
                if (_podcastTranscriptDescriptor == null)
                {
                    CurrentLyricsData = PodcastTranscriptBridge.WaitingForConnection();
                    return;
                }

                try
                {
                    var lyricsData = await _podcastTranscriptBridge.UpdateAsync(
                        LXMusic.ResolveServer(_settingsService.AppSettings.GeneralSettings.LXMusicServer),
                        _podcastTranscriptDescriptor,
                        (int)Math.Clamp(CurrentPosition.TotalMilliseconds, 0, int.MaxValue),
                        token);
                    CurrentLyricsData = lyricsData;
                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to update podcast transcript from IKUN Music");
                    CurrentLyricsData = _podcastTranscriptBridge.HasLines
                        ? _podcastTranscriptBridge.ToLyricsData(
                            (int)Math.Clamp(CurrentPosition.TotalMilliseconds, 0, int.MaxValue))
                        : PodcastTranscriptBridge.WaitingForConnection();
                    return;
                }
            }

            if (CurrentSongInfo != SongInfoExtensions.Placeholder)
            {
                int maxRetries = 3;
                CurrentLyricsSearchResult = null;

                for (int attempt = 1; attempt <= maxRetries; attempt++)
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
                    catch (Exception)
                    {
                        if (attempt == maxRetries)
                        {
                            break;
                        }

                        await Task.Delay((attempt * 2 - 1) * 1000, token);
                    }
                }

                if (CurrentLyricsSearchResult != null)
                {
                    var lyricsParser = new LyricsContentParser();

                    (CurrentLyricsData, CurrentLyricsSearchResult.TransliterationProvider, CurrentLyricsSearchResult.TranslationProvider) =
                        await Task.Run(async () => await lyricsParser.ParseAsync(
                            _translationService, _transliterationService, _settingsService.AppSettings.TranslationSettings, CurrentLyricsSearchResult, token), token);
                }
            }

            if (CurrentLyricsSearchResult == null)
            {
                CurrentLyricsData = LyricsData.GetNotfoundPlaceholder();
            }
        }

        public void UpdateLyrics()
        {
            if (IsPodcast)
            {
                QueuePodcastLyricsRefresh();
                return;
            }

            CancelPodcastLyricsRefresh();
            _ = _lyricsDebouncer.RunAsync(async (token) => await RefreshLyricsAsync(token));
        }

        private void QueuePodcastLyricsRefresh()
        {
            CancellationToken token;
            lock (_podcastLyricsRefreshLock)
            {
                _podcastLyricsRefreshPending = true;
                if (_podcastLyricsRefreshRunning) return;

                _podcastLyricsRefreshRunning = true;
                token = _podcastLyricsRefreshCts.Token;
            }

            _ = RunPodcastLyricsRefreshLoopAsync(token);
        }

        private async Task RunPodcastLyricsRefreshLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    lock (_podcastLyricsRefreshLock)
                    {
                        if (!_podcastLyricsRefreshPending) return;
                        _podcastLyricsRefreshPending = false;
                    }

                    await RefreshLyricsAsync(token);
                }
            }
            finally
            {
                var restart = false;
                lock (_podcastLyricsRefreshLock)
                {
                    _podcastLyricsRefreshRunning = false;
                    restart = _podcastLyricsRefreshPending && !_podcastLyricsRefreshCts.IsCancellationRequested;
                }

                if (restart) QueuePodcastLyricsRefresh();
            }
        }

        private void CancelPodcastLyricsRefresh()
        {
            lock (_podcastLyricsRefreshLock)
            {
                _podcastLyricsRefreshPending = false;
                _podcastLyricsRefreshCts.Cancel();
                _podcastLyricsRefreshCts.Dispose();
                _podcastLyricsRefreshCts = new CancellationTokenSource();
            }
        }
    }
}
