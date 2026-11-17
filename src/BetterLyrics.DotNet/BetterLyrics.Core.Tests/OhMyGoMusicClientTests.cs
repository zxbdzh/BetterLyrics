using BetterLyrics.Core.Implementations.Services.GsmtcService;
using BetterLyrics.Core.Helpers;
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
    public void RecognizesMyGoMediaSessionIds(string sessionId)
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

    private sealed class RecordingHandler(string body) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
