using BetterLyrics.Core.Implementations.Services;
using BetterLyrics.Core.Constants;
using BetterLyrics.Core.Helpers;
using BetterLyrics.Core.Models.Podcast;
using BetterLyrics.Core.Serialization;
using System.Net;
using System.Text.Json;
using Xunit;

namespace BetterLyrics.Core.Tests;

public sealed class PodcastTranscriptBridgeTests
{
    [Fact]
    public async Task UpdateAsync_LatePreviousContentResponse_CannotReplaceCurrentContent()
    {
        var handler = new ControlledTranscriptHandler();
        using var httpClient = new HttpClient(handler);
        var bridge = new PodcastTranscriptBridge(httpClient);

        var firstUpdate = bridge.UpdateAsync(
            "http://127.0.0.1:23330",
            Descriptor("episode-a"),
            0);
        await handler.WaitForRequestAsync("episode-a");

        var secondUpdate = bridge.UpdateAsync(
            "http://127.0.0.1:23330",
            Descriptor("episode-b"),
            0);
        await handler.WaitForRequestAsync("episode-b");
        handler.Complete("episode-b", Delta("episode-b", "content-b"));

        var current = await secondUpdate;
        handler.Complete("episode-a", Delta("episode-a", "content-a"));

        await Assert.ThrowsAsync<OperationCanceledException>(() => firstUpdate);
        Assert.Equal("episode-b", bridge.ContentId);
        Assert.Equal("content-b", Assert.Single(current.LyricsLines).PrimaryText);
        Assert.Equal("content-b", Assert.Single(bridge.ToLyricsData().LyricsLines).PrimaryText);
    }

    [Fact]
    public void ToLyricsData_StatusRows_AreNotShareableTranscriptContent()
    {
        var bridge = new PodcastTranscriptBridge();
        bridge.Apply(Delta("episode-a", "content-a", state: "preparing"));

        var partial = bridge.ToLyricsData(currentPositionMs: 120_000);
        var missing = new PodcastTranscriptBridge().ToLyricsData();
        var empty = PodcastTranscriptBridge.WaitingForConnection();

        Assert.Single(partial.LyricsLines);
        Assert.All(partial.LyricsLines, line => Assert.True(line.IsShareable));
        Assert.All(missing.LyricsLines, line => Assert.False(line.IsShareable));
        Assert.All(empty.LyricsLines, line => Assert.False(line.IsShareable));
    }

    [Theory]
    [InlineData("ikun-music-desktop")]
    [InlineData("ikun-music-desktop.exe")]
    [InlineData(@"C:\\Apps\\IKUN-MUSIC-DESKTOP.EXE")]
    [InlineData("lx-music-desktop.exe")]
    public void IsLXMusic_SupportedPlayerIds_AreRecognized(string playerId)
    {
        Assert.True(PlayerIdHelper.IsLXMusic(playerId));
    }

    [Fact]
    public void LXMusicServer_EmptyConfiguration_UsesLoopbackTranscriptSubscription()
    {
        Assert.Equal(LXMusic.DefaultServer, LXMusic.ResolveServer("  "));

        var subscription = LXMusic.GetSubscriptionUri("http://127.0.0.1:23330/");

        Assert.Equal("127.0.0.1", subscription.Host);
        Assert.Contains("contentId", subscription.Query, StringComparison.Ordinal);
        Assert.Contains("transcript", subscription.Query, StringComparison.Ordinal);
    }

    private static PodcastTranscriptDescriptor Descriptor(string contentId) => new()
    {
        ProtocolVersion = 2,
        ContentId = contentId,
        Revision = 1,
        State = "ready",
        IsPartial = false,
    };

    private static PodcastTranscriptDelta Delta(
        string contentId,
        string text,
        string state = "ready") => new()
    {
        ProtocolVersion = 2,
        ContentId = contentId,
        BaseRevision = 0,
        Revision = 1,
        Reset = true,
        State = state,
        IsPartial = state == "preparing",
        UpsertLines =
        [
            new PodcastTranscriptLine
            {
                Id = $"{contentId}-line",
                StartMs = 0,
                EndMs = 1_000,
                DisplayText = text,
            },
        ],
    };

    private sealed class ControlledTranscriptHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, TaskCompletionSource<HttpResponseMessage>> _responses = [];
        private readonly Dictionary<string, TaskCompletionSource> _requests = [];

        public Task WaitForRequestAsync(string contentId) => RequestSignal(contentId).Task;

        public void Complete(string contentId, PodcastTranscriptDelta delta)
        {
            var json = JsonSerializer.Serialize(
                delta,
                SourceGenerationContext.Default.PodcastTranscriptDelta);
            ResponseSignal(contentId).TrySetResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            });
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var contentId = GetQueryValue(request.RequestUri, "contentId");
            RequestSignal(contentId).TrySetResult();
            return ResponseSignal(contentId).Task.WaitAsync(cancellationToken);
        }

        private TaskCompletionSource RequestSignal(string contentId)
        {
            lock (_requests)
            {
                if (!_requests.TryGetValue(contentId, out var signal))
                {
                    signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    _requests[contentId] = signal;
                }
                return signal;
            }
        }

        private TaskCompletionSource<HttpResponseMessage> ResponseSignal(string contentId)
        {
            lock (_responses)
            {
                if (!_responses.TryGetValue(contentId, out var signal))
                {
                    signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    _responses[contentId] = signal;
                }
                return signal;
            }
        }

        private static string GetQueryValue(Uri? uri, string name)
        {
            var query = uri?.Query.TrimStart('?').Split('&') ?? [];
            foreach (var item in query)
            {
                var pair = item.Split('=', 2);
                if (pair.Length == 2 && pair[0] == name)
                {
                    return Uri.UnescapeDataString(pair[1]);
                }
            }
            throw new InvalidOperationException($"Missing query value: {name}");
        }
    }
}
