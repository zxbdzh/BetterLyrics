using BetterLyrics.Core.Models.Http;
using BetterLyrics.Core.Models;
using BetterLyrics.Core.Serialization;
using System.Globalization;
using System.Net.Http.Json;

namespace BetterLyrics.Core.Implementations.Services.GsmtcService;

public sealed class OhMyGoMusicClient
{
    public static bool MatchesSong(OhMyGoMusicStatus status, SongInfo song) =>
        status.TrackId != 0 &&
        string.Equals(status.Title?.Trim(), song.Title?.Trim(), StringComparison.Ordinal) &&
        string.Equals(status.Artist?.Trim(), song.Artist?.Trim(), StringComparison.Ordinal) &&
        string.Equals(status.Album?.Trim(), song.Album?.Trim(), StringComparison.Ordinal);

    public static bool SameSongKey(OhMyGoMusicSongKey left, OhMyGoMusicSongKey right) =>
        !string.IsNullOrWhiteSpace(left.Source) && !string.IsNullOrWhiteSpace(left.StableId) &&
        left.Source == right.Source && left.StableId == right.StableId;

    private static readonly HttpClient SharedHttpClient = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseProxy = false
    })
    {
        Timeout = TimeSpan.FromSeconds(5)
    };
    public static OhMyGoMusicClient Shared { get; } = new(SharedHttpClient);

    private readonly HttpClient _httpClient;

    public OhMyGoMusicClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public static Uri GetStatusStreamUri(string serverAddress) => BuildEndpoint(serverAddress, "subscribe-player-status");

    public async Task<OhMyGoMusicStatus?> GetStatusAsync(string serverAddress, CancellationToken cancellationToken = default)
    {
        var uri = BuildEndpoint(serverAddress, "status");
        using var response = await _httpClient.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(SourceGenerationContext.Default.OhMyGoMusicStatus, cancellationToken);
    }

    public async Task<OhMyGoMusicLyrics?> GetLyricsAsync(string serverAddress, ulong trackId, CancellationToken cancellationToken = default)
    {
        var baseUri = CreateLoopbackBaseUri(serverAddress);
        var uri = new UriBuilder(baseUri)
        {
            Path = baseUri.AbsolutePath.TrimEnd('/') + "/lyrics",
            Query = "trackId=" + trackId.ToString(CultureInfo.InvariantCulture)
        }.Uri;
        using var response = await _httpClient.GetAsync(uri, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        var lyrics = await response.Content.ReadFromJsonAsync(SourceGenerationContext.Default.OhMyGoMusicLyrics, cancellationToken);
        return lyrics?.TrackId == trackId ? lyrics : null;
    }

    public async Task WriteLyricsOverrideAsync(
        string serverAddress,
        OhMyGoMusicLyricsOverrideRequest request,
        CancellationToken cancellationToken = default)
    {
        var uri = BuildEndpoint(serverAddress, "lyrics-override");
        using var message = new HttpRequestMessage(HttpMethod.Put, uri)
        {
            Content = JsonContent.Create(request, SourceGenerationContext.Default.OhMyGoMusicLyricsOverrideRequest)
        };
        message.Headers.Add("X-BetterLyrics-Client", "BetterLyrics");
        using var response = await _httpClient.SendAsync(message, cancellationToken);
        if (response.IsSuccessStatusCode) return;

        var error = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            $"Oh MyGo Music rejected the lyric override ({(int)response.StatusCode}): {error}",
            null,
            response.StatusCode);
    }

    private static Uri BuildEndpoint(string serverAddress, string path)
    {
        var baseUri = CreateLoopbackBaseUri(serverAddress);
        return new UriBuilder(baseUri) { Path = baseUri.AbsolutePath.TrimEnd('/') + "/" + path }.Uri;
    }

    private static Uri CreateLoopbackBaseUri(string serverAddress)
    {
        if (!Uri.TryCreate(serverAddress.TrimEnd('/'), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp || !uri.IsLoopback || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException("The server must be a local HTTP address.", nameof(serverAddress));
        }
        return uri;
    }
}
