using BetterLyrics.Core.Enums;
using BetterLyrics.Core.Extensions;
using BetterLyrics.Core.Helpers;
using BetterLyrics.Core.Implementations.Services.GsmtcService;
using BetterLyrics.Core.Interfaces.Providers;
using BetterLyrics.Core.Interfaces.Services;
using BetterLyrics.Core.Interfaces.ViewModels;
using BetterLyrics.Core.Messages.Playback;
using BetterLyrics.Core.Models;
using BetterLyrics.Core.Models.Entities;
using BetterLyrics.Core.Models.Http;
using BetterLyrics.Core.Models.Lyrics;
using BetterLyrics.Core.Models.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Net;
using LyricsContentParser = BetterLyrics.Core.Helpers.Lyrics.ContentParser.LyricsContentParser;

namespace BetterLyrics.Core.ViewModels;

/// <summary>
/// Singleton ViewModel
/// </summary>
public partial class LyricsSearchControlViewModel : ObservableRecipient, ISingletonViewModel,
    IRecipient<CurrentSongInfoChangedMessage>
{
    private readonly IAppUIThreadProvider _appUIThreadProvider;
    private readonly IGlobalToastProvider _globalToastProvider;
    private readonly ILyricsSearchService _lyricsSearchService;
    private readonly ISettingsService _settingsService;
    private readonly ISongSearchMapService _songSearchMapService;
    private readonly ILogger<LyricsSearchControlViewModel> _logger;
    private SongInfo? _searchSong;
    private OhMyGoMusicStatus? _searchMyGoStatus;
    private readonly ILocalizationService _localizationService;

    public IGsmtcService GsmtcService { get; set; }

    [ObservableProperty] public partial AppSettings AppSettings { get; set; }

    [ObservableProperty] public partial ObservableCollection<LyricsCacheItem> LyricsSearchResults { get; set; } = [];

    [ObservableProperty] public partial LyricsCacheItem? SelectedLyricsSearchResult { get; set; }

    [ObservableProperty] public partial ObservableCollection<LyricsData>? LyricsDataArr { get; set; }

    [ObservableProperty] public partial int SelectedTrackIndex { get; set; } = 0;

    [ObservableProperty]
    public partial MappedSongSearchQuery? MappedSongSearchQuery { get; set; }

    [ObservableProperty] public partial bool IsSearching { get; set; } = false;

    public LyricsSearchControlViewModel(
    ILyricsSearchService lyricsSearchService,
    IGsmtcService gsmtcService,
    ISettingsService settingsService,
    ISongSearchMapService songSearchMapService,
    IAppUIThreadProvider appUiThreadProvider,
    IGlobalToastProvider globalToastProvider,
    ILogger<LyricsSearchControlViewModel> logger, ILocalizationService localizationService)
    {
        _lyricsSearchService = lyricsSearchService;
        _settingsService = settingsService;
        _songSearchMapService = songSearchMapService;
        _appUIThreadProvider = appUiThreadProvider;
        _globalToastProvider = globalToastProvider;
        _logger = logger;
        _localizationService = localizationService;

        GsmtcService = gsmtcService;
        AppSettings = _settingsService.AppSettings;

        _ = InitMappedSongSearchQueryAsync();

        IsActive = true;
    }

    private async Task InitMappedSongSearchQueryAsync()
    {
        LyricsSearchResults.Clear();
        LyricsDataArr = null;
        SelectedLyricsSearchResult = null;
        _searchSong = null;
        _searchMyGoStatus = null;
        if (GsmtcService.CurrentSongInfo != SongInfoExtensions.Placeholder)
        {
            var found = await _songSearchMapService.TryGetMappingAsync(GsmtcService.CurrentSongInfo);

            if (found == null)
                MappedSongSearchQuery = new MappedSongSearchQuery
                {
                    OriginalTitle = GsmtcService.CurrentSongInfo.Title,
                    OriginalArtist = GsmtcService.CurrentSongInfo.Artist,
                    OriginalAlbum = GsmtcService.CurrentSongInfo.Album,
                    MappedTitle = GsmtcService.CurrentSongInfo.Title,
                    MappedArtist = GsmtcService.CurrentSongInfo.Artist,
                    MappedAlbum = GsmtcService.CurrentSongInfo.Album
                };
            else
                MappedSongSearchQuery = (MappedSongSearchQuery)found.Clone();
        }
    }

    public void PlayLyricsLine(LyricsLine? value)
    {
        if (value?.StartMs == null) return;

        _ = GsmtcService.ChangePositionAsync(value.StartMs / 1000.0);
    }

    partial void OnMappedSongSearchQueryChanged(MappedSongSearchQuery? value)
    {
        WeakReferenceMessenger.Default.Send(new MappedSongSearchQueryChangedMessage(value));
    }

    partial void OnSelectedLyricsSearchResultChanged(LyricsCacheItem? value)
    {
        MappedSongSearchQuery?.LyricsSearchProvider = value?.Provider;
        if (value?.Raw != null)
        {
            LyricsDataArr = [.. value.LyricsDataArr ?? []];
            SelectedTrackIndex = 0;
        }
        else
        {
            LyricsDataArr = null;
        }
    }

    [RelayCommand]
    private void Search()
    {
        if (MappedSongSearchQuery == null) return;

        IsSearching = true;
        LyricsSearchResults.Clear();
        MappedSongSearchQuery.LyricsSearchProvider = null;
        var searchedSong = (SongInfo)GsmtcService.CurrentSongInfo.Clone();
        _searchSong = searchedSong;
        _searchMyGoStatus = GsmtcService.CurrentOhMyGoMusicStatus;
        var searchQuery = (MappedSongSearchQuery)MappedSongSearchQuery.Clone();

        var activeProviders = _lyricsSearchService.GetActiveProviders();
        foreach (var provider in activeProviders)
            LyricsSearchResults.Add(new LyricsCacheItem
            {
                Provider = provider,
                IsSearching = true
            });

        _ = Task.Run(async () =>
        {
            try
            {
                if (PlayerIdHelper.IsOhMyGoMusic(searchedSong.PlayerId) && _searchMyGoStatus == null)
                {
                    try
                    {
                        var snapshot = await OhMyGoMusicClient.Shared.GetStatusAsync(AppSettings.GeneralSettings.OhMyGoMusicServer);
                        _appUIThreadProvider.Execute(() =>
                        {
                            if (ReferenceEquals(_searchSong, searchedSong) && snapshot != null &&
                                OhMyGoMusicClient.MatchesSong(snapshot, searchedSong)) _searchMyGoStatus = snapshot;
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Oh MyGo Music is unavailable while searching; keep manual lyrics locally.");
                    }
                }
                var songInfo = ((SongInfo)searchedSong.Clone())
                    .WithTitle(searchQuery.MappedTitle)
                    .WithArtist(searchQuery.MappedArtist)
                    .WithAlbum(searchQuery.MappedAlbum);

                var checkCache = !_settingsService.AppSettings.GeneralSettings.IgnoreCacheWhenSearching;

                await foreach (var item in _lyricsSearchService.SearchAllAsync(songInfo, checkCache, CancellationToken.None))
                {
                    var index = -1;
                    for (var i = 0; i < LyricsSearchResults.Count; i++)
                    {
                        if (LyricsSearchResults[i].Provider == item.Provider)
                        {
                            index = i;
                            break;
                        }
                    }

                    if (index != -1)
                    {
                        var parser = new LyricsContentParser();
                        await parser.ParseAsync(item, CancellationToken.None);
                        _appUIThreadProvider.Execute(() =>
                        {
                            if (!ReferenceEquals(_searchSong, searchedSong)) return;
                            LyricsSearchResults[index] = item;
                            item.IsSearching = false;
                            item.LyricsDataArr = parser.LyricsDataArr;
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Search: An error occurred while searching for lyrics.");
            }
            finally
            {
                _appUIThreadProvider.Execute(() =>
                {
                    if (!ReferenceEquals(_searchSong, searchedSong)) return;
                    for (var i = LyricsSearchResults.Count - 1; i >= 0; i--)
                    {
                        var result = LyricsSearchResults[i];
                        if (result.IsSearching)
                        {
                            result.IsSearching = false;
                        }
                    }
                });
            }
            _appUIThreadProvider.Execute(() =>
            {
                if (!ReferenceEquals(_searchSong, searchedSong)) return;
                IsSearching = false;
            });
        });
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (MappedSongSearchQuery == null) return;
        var mapping = (MappedSongSearchQuery)MappedSongSearchQuery.Clone();
        var currentSong = GsmtcService.CurrentSongInfo;
        var expected = _searchMyGoStatus;
        var selected = SelectedLyricsSearchResult;
        var isMyGo = PlayerIdHelper.IsOhMyGoMusic(currentSong.PlayerId);
        OhMyGoMusicLyrics? payload = null;
        if (isMyGo && selected is { IsFound: true } && !mapping.IsMarkedAsPureMusic)
        {
            if (_searchSong?.StartedAt != currentSong.StartedAt)
            {
                _globalToastProvider.Show("OhMyGoMusicLyricsTrackChanged", null, MessageSeverity.Warning);
                return;
            }
            var parser = new LyricsContentParser();
            var tracks = await parser.ParseForSynchronizationAsync(selected, CancellationToken.None);
            payload = OhMyGoMusicLyricsAdapter.ToWriteModel(selected, tracks);
            mapping.ManualLyrics = OhMyGoMusicLyricsAdapter.ToCacheItem(payload);
            mapping.ManualLyrics.CopyFromSongInfo(currentSong);
            mapping.MyGoOverrideSaved = false;
        }
        await _songSearchMapService.SaveMappingAsync(mapping);
        if (payload != null)
        {
            try
            {
                await GsmtcService.WriteCurrentOhMyGoMusicLyricsAsync(expected, currentSong, payload);
                mapping.MyGoOverrideSaved = true;
                await _songSearchMapService.SaveMappingAsync(mapping);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to sync manually selected lyrics to Oh MyGo Music.");
                var reason = ex is HttpRequestException { StatusCode: HttpStatusCode.Forbidden }
                    ? "OhMyGoMusicLyricsWriteDisabled"
                    : ex is HttpRequestException { StatusCode: HttpStatusCode.Conflict }
                        ? "OhMyGoMusicLyricsTrackChanged"
                        : "OhMyGoMusicLyricsSavedLocally";
                _globalToastProvider.Show("OhMyGoMusicLyricsSyncFailed",
                    _localizationService.GetLocalizedString(reason), MessageSeverity.Warning);
            }
        }
        if (GsmtcService.CurrentSongInfo.StartedAt == currentSong.StartedAt)
            MappedSongSearchQuery = mapping;
        GsmtcService.UpdateLyrics();
    }

    [RelayCommand]
    private async Task ResetAsync()
    {
        if (MappedSongSearchQuery == null) return;

        await _songSearchMapService.DeleteMappingAsync(MappedSongSearchQuery);
        await InitMappedSongSearchQueryAsync();
        SelectedLyricsSearchResult = null;
        GsmtcService.UpdateLyrics();
    }

    [RelayCommand]
    private void ResetMappedTitle()
    {
        MappedSongSearchQuery?.MappedTitle = MappedSongSearchQuery?.OriginalTitle ?? string.Empty;
    }

    [RelayCommand]
    private void ResetMappedArtist()
    {
        MappedSongSearchQuery?.MappedArtist = MappedSongSearchQuery?.OriginalArtist ?? string.Empty;
    }

    [RelayCommand]
    private void ResetMappedAlbum()
    {
        MappedSongSearchQuery?.MappedAlbum = MappedSongSearchQuery?.OriginalAlbum ?? string.Empty;
    }

    [RelayCommand]
    private async Task CopySearchLinkAsync()
    {
        var uriString = $"betterlyrics://lyrics/search/" +
                        $"title={WebUtility.UrlEncode(MappedSongSearchQuery?.MappedTitle)}&" +
                        $"artist={WebUtility.UrlEncode(MappedSongSearchQuery?.MappedArtist)}&" +
                        $"album={WebUtility.UrlEncode(MappedSongSearchQuery?.MappedAlbum)}";
        try
        {
            await TextCopy.ClipboardService.SetTextAsync(uriString);

            _globalToastProvider.Show("ActionCompleted", null, MessageSeverity.Success);
        }
        catch (Exception ex)
        {
            _globalToastProvider.Show("Error", ex.Message, MessageSeverity.Error);
        }
    }

    public void Receive(CurrentSongInfoChangedMessage message)
    {
        _ = InitMappedSongSearchQueryAsync();
    }
}
