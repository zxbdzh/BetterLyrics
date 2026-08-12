using BetterLyrics.Core.Implementations.Services;
using BetterLyrics.Core.Models.Podcast;
using BetterLyrics.Core.Serialization;
using System.Net;
using System.Text.Json;
using Xunit;

namespace BetterLyrics.Core.Tests;

public sealed class PodcastLongFormContentBridgeTests
{
    [Fact]
    public async Task LoadAsync_ValidDocument_IsCachedAndUsesExpectedEndpoint()
    {
        var document = Document("article-1", revision: 0xFFFFFFFFFFFF);
        var handler = new RecordingHandler(document);
        using var httpClient = new HttpClient(handler);
        var bridge = new PodcastLongFormContentBridge(httpClient);

        var first = await bridge.LoadAsync("http://127.0.0.1:23330", Descriptor(document));
        var second = await bridge.LoadAsync("http://127.0.0.1:23330", Descriptor(document));

        Assert.Same(first, second);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("/long-form-content", handler.RequestUri?.AbsolutePath);
        Assert.Equal("?contentId=article-1", handler.RequestUri?.Query);
        Assert.Equal(0xFFFFFFFFFFFF, first.Revision);
    }

    [Theory]
    [InlineData("https://example.com:23330")]
    [InlineData("file:///C:/temp")]
    [InlineData("http://user:password@127.0.0.1:23330")]
    public async Task LoadAsync_NonLoopbackOrUnsafeServer_IsRejected(string serverAddress)
    {
        var document = Document("article-1");
        var bridge = new PodcastLongFormContentBridge(new HttpClient(new RecordingHandler(document)));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            bridge.LoadAsync(serverAddress, Descriptor(document)));
    }

    [Fact]
    public async Task LoadAsync_DocumentDoesNotMatchDescriptor_IsRejected()
    {
        var document = Document("article-2");
        var bridge = new PodcastLongFormContentBridge(
            new HttpClient(new RecordingHandler(document)));

        await Assert.ThrowsAsync<InvalidDataException>(() => bridge.LoadAsync(
            "http://localhost:23330",
            Descriptor(Document("article-1"))));
    }

    [Fact]
    public async Task LoadAsync_LatePreviousContentResponse_CannotReplaceCurrentContent()
    {
        var handler = new ControlledHandler();
        using var httpClient = new HttpClient(handler);
        var bridge = new PodcastLongFormContentBridge(httpClient);
        var firstDocument = Document("article-1");
        var secondDocument = Document("article-2");

        var firstLoad = bridge.LoadAsync(
            "http://127.0.0.1:23330",
            Descriptor(firstDocument));
        await handler.WaitForRequestAsync("article-1");

        var secondLoad = bridge.LoadAsync(
            "http://127.0.0.1:23330",
            Descriptor(secondDocument));
        await handler.WaitForRequestAsync("article-2");
        handler.Complete(secondDocument);
        Assert.Equal("article-2", (await secondLoad).ContentId);

        handler.Complete(firstDocument);
        await Assert.ThrowsAsync<OperationCanceledException>(() => firstLoad);

        var cached = await bridge.LoadAsync(
            "http://127.0.0.1:23330",
            Descriptor(secondDocument));
        Assert.Equal("article-2", cached.ContentId);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public void ToShareableLyricsLines_PreservesReadableStructureWithoutTimeline()
    {
        var document = Document("article-1");

        var lines = PodcastLongFormContentBridge.ToShareableLyricsLines(document);

        Assert.Collection(
            lines,
            line =>
            {
                Assert.Equal("A heading", line.PrimaryText);
                Assert.Equal("long-form:heading", line.AgentId);
            },
            line =>
            {
                Assert.Equal("- A list item", line.PrimaryText);
                Assert.Equal("long-form:list-item", line.AgentId);
            },
            line =>
            {
                Assert.Equal("> A quotation", line.PrimaryText);
                Assert.Equal("long-form:quote", line.AgentId);
            });
        Assert.All(lines, line =>
        {
            Assert.Equal(0, line.StartMs);
            Assert.Null(line.EndMs);
            Assert.False(line.IsPrimaryHasRealSyllableInfo);
            Assert.True(line.IsShareable);
            Assert.Empty(line.PrimarySyllables);
        });
    }

    private static PodcastLongFormContentDescriptor Descriptor(
        PodcastLongFormContentDocument document) => new()
        {
            ProtocolVersion = document.ProtocolVersion,
            ContentId = document.ContentId,
            Revision = document.Revision,
            BlockCount = document.BlockCount,
            CharacterCount = document.CharacterCount,
        };

    private static PodcastLongFormContentDocument Document(
        string contentId,
        long revision = 1)
    {
        List<PodcastLongFormContentBlock> blocks =
        [
            new() { Id = "heading", Kind = "heading", Text = "A heading", Level = 2 },
            new() { Id = "list", Kind = "list-item", Text = "A list item" },
            new() { Id = "quote", Kind = "quote", Text = "A quotation" },
        ];
        return new PodcastLongFormContentDocument
        {
            ProtocolVersion = 1,
            ContentId = contentId,
            Revision = revision,
            Title = "Article title",
            Blocks = blocks,
            BlockCount = blocks.Count,
            CharacterCount = blocks.Sum(block => block.Text.Length),
            OriginalUrl = $"https://example.com/{contentId}",
            AudioUrl = $"https://cdn.example.com/{contentId}.mp3",
            ShareUrl = $"https://example.com/{contentId}",
        };
    }

    private sealed class RecordingHandler(PodcastLongFormContentDocument document)
        : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestUri = request.RequestUri;
            return Task.FromResult(Response(document));
        }
    }

    private sealed class ControlledHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, TaskCompletionSource<HttpResponseMessage>> _responses = [];
        private readonly Dictionary<string, TaskCompletionSource> _requests = [];

        public int RequestCount { get; private set; }

        public Task WaitForRequestAsync(string contentId) => RequestSignal(contentId).Task;

        public void Complete(PodcastLongFormContentDocument document) =>
            ResponseSignal(document.ContentId).TrySetResult(Response(document));

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
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
    }

    private static HttpResponseMessage Response(PodcastLongFormContentDocument document)
    {
        var json = JsonSerializer.Serialize(
            document,
            SourceGenerationContext.Default.PodcastLongFormContentDocument);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
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
