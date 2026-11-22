using BetterLyrics.Core.Implementations.Services.GsmtcService;
using BetterLyrics.Core.Helpers;
using BetterLyrics.Core.Models;
using BetterLyrics.Core.Models.Http;
using System.Text.Json;
using System.Net;
using System.Text;
using Xunit;

namespace BetterLyrics.Core.Tests;

public class OhMyGoMusicClientTests
{
    [Theory]
    [InlineData("MyGo Music.exe")]
    [InlineData("dev.egoist.music")]
    [InlineData("MyGo Music Dev.exe")]
    [InlineData("dev.egoist.music.dev")]
    [InlineData("Oh MyGo Music.exe")]
    [InlineData("Oh MyGo Music Dev.exe")]
    public void RecognizesOhMyGoMusicMediaSessionIds(string sessionId)
    {
        Assert.True(PlayerIdHelper.IsOhMyGoMusic(sessionId));
    }

    [Fact]
    public async Task FetchesCurrentLyricsWithTrackRevisionAndWordTimes()
    {
        const string responseBody = """
            {"trackId":7,"lrc":"[00:01.000]hello world","translation":"[00:01.000]你好世界","lines":[{"start":1000,"end":3000,"text":"hello world","translation":"你好世界","words":[{"start":1000,"end":2000,"text":"hello "},{"start":2000,"end":3000,"text":"world"}]}]}
            """;
        var handler = new RecordingHandler(responseBody);
        using var httpClient = new HttpClient(handler);
        var client = new OhMyGoMusicClient(httpClient);

        var lyrics = await client.GetLyricsAsync("http://127.0.0.1:23332", 7);

        Assert.Equal("http://127.0.0.1:23332/lyrics?trackId=7", handler.RequestUri?.ToString());
        Assert.NotNull(lyrics);
        Assert.Equal(7UL, lyrics.TrackId);
        Assert.Equal(3000, Assert.Single(lyrics.Lines).Words[1].End);
    }

    [Fact]
    public async Task ReadsCurrentPlaybackStatus()
    {
        using var httpClient = new HttpClient(new RecordingHandler(
            "{\"trackId\":8,\"title\":\"Awake\",\"artist\":\"{{PERSON_3q5gtb8s}}\",\"album\":\"Moon\",\"playing\":true,\"positionMs\":4300,\"durationMs\":210000,\"lyricsReady\":true}"));
        var client = new OhMyGoMusicClient(httpClient);

        var status = await client.GetStatusAsync("http://127.0.0.1:23332");

        Assert.NotNull(status);
        Assert.Equal(8UL, status.TrackId);
        Assert.Equal(4300, status.PositionMs);
        Assert.True(status.Playing);
        Assert.True(status.LyricsReady);
    }

    [Fact]
    public async Task DisplaysLrcWhenMyGoOmitsOptionalWordAndTranslationFields()
    {
        using var httpClient = new HttpClient(new RecordingHandler(
            "{\"trackId\":2,\"lrc\":\"[00:00.000]同步测试B 第1句\\n[00:05.000]同步测试B 第2句\\n\"}"));
        var client = new OhMyGoMusicClient(httpClient);

        var source = await client.GetLyricsAsync("http://127.0.0.1:23332", 2);
        var item = OhMyGoMusicLyricsAdapter.ToCacheItem(Assert.IsType<BetterLyrics.Core.Models.Http.OhMyGoMusicLyrics>(source));

        Assert.Equal("[00:00.000]同步测试B 第1句\n[00:05.000]同步测试B 第2句\n", item.Raw);
        Assert.True(string.IsNullOrEmpty(item.Translation));
    }

    [Fact]
    public async Task RejectsLyricsForDifferentTrackRevision()
    {
        using var httpClient = new HttpClient(new RecordingHandler("{\"trackId\":6,\"lrc\":\"stale\"}"));
        var client = new OhMyGoMusicClient(httpClient);

        var lyrics = await client.GetLyricsAsync("http://127.0.0.1:23332", 7);

        Assert.Null(lyrics);
    }

    [Fact]
    public async Task RejectsNonLoopbackServerAddresses()
    {
        using var httpClient = new HttpClient(new RecordingHandler("{}"));
        var client = new OhMyGoMusicClient(httpClient);

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetStatusAsync("http://example.com"));
    }

    [Fact]
    public async Task WritesStableSongIdentityAndClientHeaderWithLyrics()
    {
        var handler = new RecordingHandler("{}");
        using var httpClient = new HttpClient(handler);
        var client = new OhMyGoMusicClient(httpClient);
        await client.WriteLyricsOverrideAsync("http://127.0.0.1:23332", new OhMyGoMusicLyricsOverrideRequest
        {
            TrackId = 7,
            InstanceId = "current-instance",
            SongKey = new() { Source = "local", StableId = "C:\\Music\\song.mp3" },
            Lyrics = new() { Lrc = "[00:01.234]hello" }
        });
        Assert.Equal(HttpMethod.Put, handler.Method);
        Assert.Equal("BetterLyrics", handler.ClientHeader);
        Assert.Equal("http://127.0.0.1:23332/lyrics-override", handler.RequestUri!.ToString());
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("current-instance", body.RootElement.GetProperty("instanceId").GetString());
        Assert.Equal("C:\\Music\\song.mp3", body.RootElement.GetProperty("songKey").GetProperty("stableId").GetString());
        Assert.Equal("[00:01.234]hello", body.RootElement.GetProperty("lyrics").GetProperty("lrc").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task PreservesWriteFailureStatusForUserFeedback(HttpStatusCode status)
    {
        using var httpClient = new HttpClient(new RecordingHandler("{\"code\":\"write_disabled\"}", status));
        var client = new OhMyGoMusicClient(httpClient);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.WriteLyricsOverrideAsync("http://127.0.0.1:23332", new()));
        Assert.Equal(status, error.StatusCode);
    }

    [Fact]
    public void RejectsDifferentSongMetadataAndSourceIdentity()
    {
        var status = new OhMyGoMusicStatus { TrackId = 7, Title = "Song", Artist = "Singer", Album = "Album" };
        Assert.True(OhMyGoMusicClient.MatchesSong(status, new SongInfo { Title = "Song", Artist = "Singer", Album = "Album" }));
        Assert.False(OhMyGoMusicClient.MatchesSong(status, new SongInfo { Title = "Other", Artist = "Singer", Album = "Album" }));
        Assert.False(OhMyGoMusicClient.SameSongKey(new() { Source = "one", StableId = "7" }, new() { Source = "two", StableId = "7" }));
        Assert.False(OhMyGoMusicClient.SameSongKey(new(), new()));
    }

    private sealed class RecordingHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? Body { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? ClientHeader { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Method = request.Method;
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            ClientHeader = request.Headers.TryGetValues("X-BetterLyrics-Client", out var values) ? values.Single() : null;
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }
}
