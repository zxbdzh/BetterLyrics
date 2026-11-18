using BetterLyrics.Core.Constants;
using BetterLyrics.Core.Enums;
using BetterLyrics.Core.Events;
using BetterLyrics.Core.Interfaces.Providers;
using BetterLyrics.Core.Interfaces.Services;
using BetterLyrics.Core.Models;
using Hqub.Lastfm;
using Hqub.Lastfm.Entities;
using Microsoft.Extensions.Logging;

namespace BetterLyrics.Core.Implementations.Services;

/// <summary>
/// Singleton Service
/// </summary>
public class LastFmService : ILastFmService
{
    private readonly LastfmClient? _client;
    private readonly IGlobalToastProvider _globalToastProvider;
    private readonly ILastFmDialogProvider _lastFmDialogProvider;
    private readonly IPasswordVaultProvider _passwordVaultProvider;
    private readonly ISettingsService _settingsService;
    private readonly ISongSearchMapService _songSearchMapService;
    private readonly ILauncherProvider _launcherProvider;
    private readonly ILogger<LastFmService> _logger;
    private string? _sessionKey;

    public event EventHandler<LastFMUserChangedEventArgs>? UserChanged;
    public event EventHandler<LastFMIsAuthenticatedChangedEventArgs>? IsAuthenticatedChanged;

    public User? User { get; private set; }

    public bool IsAuthenticated { get; private set; }

    public static bool HasConfiguredCredentials(string? apiKey, string? sharedSecret)
    {
        return !string.IsNullOrWhiteSpace(apiKey) &&
               !string.IsNullOrWhiteSpace(sharedSecret) &&
               apiKey.Trim() != "Your api key here" &&
               sharedSecret.Trim() != "Your shared secret here";
    }

    public LastFmService(ISettingsService settingsService,
        ISongSearchMapService songSearchMapService, IPasswordVaultProvider passwordVaultProvider,
        IGlobalToastProvider globalToastProvider,
        ILauncherProvider launcherProvider, ILastFmDialogProvider lastFmDialogProvider,
        ILogger<LastFmService> logger)
    {
        _settingsService = settingsService;
        _songSearchMapService = songSearchMapService;
        _passwordVaultProvider = passwordVaultProvider;
        _globalToastProvider = globalToastProvider;
        _launcherProvider = launcherProvider;
        _lastFmDialogProvider = lastFmDialogProvider;
        _logger = logger;

        // 无密钥发行不创建客户端，也不恢复旧会话，避免启动时发送无效请求。
        if (!HasConfiguredCredentials(LastFM.ApiKey, LastFM.SharedSecret))
        {
            _logger.LogInformation("Last.fm is unavailable in this build because its credentials are not configured.");
            return;
        }
        _client = new LastfmClient(LastFM.ApiKey, LastFM.SharedSecret);
        _sessionKey = _passwordVaultProvider.Get(Core.Constants.App.AppName, LastFM.SessionKeyCredentialKey);
        _logger.LogDebug("Last.fm service initialized. Session key present: {HasSessionKey}.",
            !string.IsNullOrEmpty(_sessionKey));
        _ = UpdateAuthStatusAsync();
    }

    public async Task ConfirmAuthAsync()
    {
        if (_client == null)
        {
            _globalToastProvider.Show("LastFMUnavailable", severity: MessageSeverity.Warning);
            return;
        }
        try
        {
            await _client.AuthenticateViaWebAsync();
            if (_client.Session != null && _client.Session.Authenticated)
            {
                _sessionKey = _client.Session.SessionKey;
                _passwordVaultProvider.Save(Core.Constants.App.AppName, LastFM.SessionKeyCredentialKey, _sessionKey);
                _logger.LogInformation("Last.fm authentication succeeded.");
                await UpdateAuthStatusAsync();
                return;
            }
            throw new Exception("Authentication failed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Last.fm authentication failed.");
            _globalToastProvider.Show("LastFMAuthFailed", ex.Message, MessageSeverity.Error);
        }
    }

    public async Task ConfirmUnAuthAsync()
    {
        _sessionKey = null;
        _passwordVaultProvider.Delete(Core.Constants.App.AppName, LastFM.SessionKeyCredentialKey);
        _logger.LogInformation("Last.fm authentication cleared by the user.");
        await UpdateAuthStatusAsync();
    }

    public async Task AuthAsync()
    {
        if (_client == null)
        {
            _globalToastProvider.Show("LastFMUnavailable", severity: MessageSeverity.Warning);
            return;
        }
        try
        {
            var url = await _client.GetWebAuthenticationUrlAsync();

            // 启动器任务也可能失败，必须等待它并纳入本次登录异常处理。
            await _launcherProvider.LaunchUriAsync(new Uri(url));

            await _lastFmDialogProvider.ShowAuthDialogAsync(ConfirmAuthAsync);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start Last.fm authentication.");
            _globalToastProvider.Show("LastFMAuthFailed", ex.Message, MessageSeverity.Error);
        }
    }

    public async Task UnAuthAsync()
    {
        if (_client == null)
        {
            _globalToastProvider.Show("LastFMUnavailable", severity: MessageSeverity.Warning);
            return;
        }
        await _launcherProvider.LaunchUriAsync(new Uri(LastFM.UnAuthUrl));
        await _lastFmDialogProvider.ShowUnAuthDialogAsync(ConfirmUnAuthAsync);
    }

    public async Task TrackAsync(SongInfo songInfo)
    {
        if (_client != null && IsAuthenticated)
        {
            var (mappedTitle, mappedArtist, mappedAlbum) = await _songSearchMapService.GetMappingAsync(songInfo);

            try
            {
                var scrobble = new Scrobble
                {
                    Track = mappedTitle,
                    Artist = mappedArtist,
                    Album = mappedAlbum,
                    Date = DateTime.UtcNow
                };
                var resp = await _client.Track.ScrobbleAsync(scrobble);
                if (resp != null && resp.Accepted == 0)
                {
                    _logger.LogWarning("Last.fm scrobble was not accepted. Ignored: {Ignored}.", resp.Ignored);
                    _globalToastProvider.Show("Error", resp.Ignored > 0 ? "Scrobble ignored" : "Scrobble failed", MessageSeverity.Error);
                }
                else
                {
                    _logger.LogInformation("Scrobbled to Last.fm: {Title} - {Artist}.", mappedTitle, mappedArtist);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to scrobble to Last.fm: {Title} - {Artist}.", mappedTitle,
                    mappedArtist);
                _globalToastProvider.Show("Error", ex.Message, MessageSeverity.Error);
            }
        }
        else
        {
            _logger.LogDebug("Skipped Last.fm scrobble because the service is not authenticated.");
        }
    }

    public async Task UpdateNowPlayingAsync(SongInfo songInfo)
    {
        if (_client != null && IsAuthenticated)
        {
            var (mappedTitle, mappedArtist, mappedAlbum) = await _songSearchMapService.GetMappingAsync(songInfo);

            try
            {
                var resp = await _client.Track.UpdateNowPlayingAsync(mappedTitle, mappedArtist, 0, mappedAlbum);
                if (!resp)
                {
                    _logger.LogWarning("Failed to update Last.fm now playing: {Title} - {Artist}.", mappedTitle,
                        mappedArtist);
                    _globalToastProvider.Show("Error", "Update now playing failed", MessageSeverity.Error);
                }
                else
                {
                    _logger.LogDebug("Updated Last.fm now playing: {Title} - {Artist}.", mappedTitle, mappedArtist);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update Last.fm now playing: {Title} - {Artist}.", mappedTitle,
                    mappedArtist);
                _globalToastProvider.Show("Error", ex.Message, MessageSeverity.Error);
            }
        }
    }

    public async Task RefreshAsync()
    {
        await UpdateAuthStatusAsync();
    }

    private async Task UpdateAuthStatusAsync()
    {
        IsAuthenticated = _client != null && !string.IsNullOrEmpty(_sessionKey);
        if (_client != null && IsAuthenticated)
        {
            _client.Session.SessionKey = _sessionKey;
        }
        IsAuthenticatedChanged?.Invoke(this, new LastFMIsAuthenticatedChangedEventArgs(IsAuthenticated));
        if (_client != null && IsAuthenticated)
        {
            try
            {
                User = await _client.User.GetInfoAsync(null);
                _logger.LogInformation("Last.fm user information loaded: {UserName}.", User?.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load Last.fm user information; treating the session as unavailable.");
                _globalToastProvider.Show("Error", ex.Message, MessageSeverity.Error);
                User = null;
            }
        }
        else
        {
            User = null;
            _logger.LogDebug("Last.fm is not authenticated.");
        }

        UserChanged?.Invoke(this, new LastFMUserChangedEventArgs(User));
    }

    public async Task<string?> GetAlbumArtUrlAsync(SongInfo songInfo)
    {
        if (_client == null) return null;
        try
        {
            var artist = songInfo.Artist ?? "";
            var album = songInfo.Album ?? "";
            var track = songInfo.Title ?? "";

            if (!string.IsNullOrWhiteSpace(album))
            {
                var albumInfo = await _client.Album.GetInfoAsync(artist, album);
                if (albumInfo?.Images != null && albumInfo.Images.Count > 0)
                {
                    var imgUrl = albumInfo.Images[albumInfo.Images.Count - 1].Url;
                    if (!string.IsNullOrEmpty(imgUrl) && imgUrl.Contains("http"))
                    {
                        return imgUrl;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(track))
            {
                var trackInfo = await _client.Track.GetInfoAsync(track, artist);
                if (trackInfo?.Album?.Images != null && trackInfo.Album.Images.Count > 0)
                {
                    var imgUrl = trackInfo.Album.Images[trackInfo.Album.Images.Count - 1].Url;
                    if (!string.IsNullOrEmpty(imgUrl) && imgUrl.Contains("http"))
                    {
                        return imgUrl;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to fetch album art from Last.fm for {Title} - {Artist}.", songInfo.Title,
                songInfo.Artist);
        }

        return null;
    }
}
