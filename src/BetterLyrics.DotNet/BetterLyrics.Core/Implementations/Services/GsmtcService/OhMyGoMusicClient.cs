using BetterLyrics.Core.Models.Http;
using BetterLyrics.Core.Serialization;
using System.Globalization;
using System.Net.Http.Json;

namespace BetterLyrics.Core.Implementations.Services.GsmtcService;

public sealed class OhMyGoMusicClient
{
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
