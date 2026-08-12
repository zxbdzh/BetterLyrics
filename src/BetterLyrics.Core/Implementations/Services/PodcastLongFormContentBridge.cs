using BetterLyrics.Core.Models.Lyrics;
using BetterLyrics.Core.Models.Podcast;
using BetterLyrics.Core.Serialization;
using System.Net.Http.Json;

namespace BetterLyrics.Core.Implementations.Services
{
    public sealed class PodcastLongFormContentBridge
    {
        private static readonly HashSet<string> BlockKinds =
            ["heading", "paragraph", "list-item", "quote"];
        private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(5) };
        private readonly object _sync = new();
        private readonly HttpClient _httpClient;
        private PodcastLongFormContentDocument? _document;
        private string _requestedContentId = "";
        private long _requestedRevision = -1;
        private long _generation;

        public PodcastLongFormContentBridge(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? SharedHttpClient;
        }

        public void Reset()
        {
            lock (_sync) ResetCore();
        }

        public async Task<PodcastLongFormContentDocument> LoadAsync(
            string serverAddress,
            PodcastLongFormContentDescriptor descriptor,
            CancellationToken cancellationToken = default)
        {
            ValidateDescriptor(descriptor);

            long generation;
            lock (_sync)
            {
                if (!string.Equals(_requestedContentId, descriptor.ContentId, StringComparison.Ordinal) ||
                    _requestedRevision != descriptor.Revision)
                {
                    ResetCore();
                    _requestedContentId = descriptor.ContentId;
                    _requestedRevision = descriptor.Revision;
                }

                if (_document != null) return _document;
                generation = _generation;
            }

            var document = await _httpClient.GetFromJsonAsync(
                BuildRequestUri(serverAddress, descriptor.ContentId),
                SourceGenerationContext.Default.PodcastLongFormContentDocument,
                cancellationToken) ?? throw new InvalidDataException("IKUN returned an empty long-form content response.");
            ValidateDocument(document, descriptor);

            lock (_sync)
            {
                ThrowIfSuperseded(generation, descriptor, cancellationToken);
                _document = document;
                return document;
            }
        }

        public static IReadOnlyList<LyricsLine> ToShareableLyricsLines(
            PodcastLongFormContentDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);
            ValidateDocument(document, document);

            return document.Blocks.Select(block => new LyricsLine
            {
                PrimaryText = FormatBlockText(block),
                AgentId = $"long-form:{block.Kind}",
                IsPrimaryHasRealSyllableInfo = false,
                IsShareable = true,
            }).ToList();
        }

        private void ResetCore()
        {
            _generation++;
            _requestedContentId = "";
            _requestedRevision = -1;
            _document = null;
        }

        private static Uri BuildRequestUri(string serverAddress, string contentId)
        {
            if (!Uri.TryCreate(serverAddress.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri) ||
                !baseUri.IsLoopback ||
                (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps) ||
                !string.IsNullOrEmpty(baseUri.UserInfo))
            {
                throw new InvalidDataException("IKUN long-form content endpoint must use a loopback HTTP address.");
            }

            return new Uri(
                baseUri,
                $"long-form-content?contentId={Uri.EscapeDataString(contentId)}");
        }

        private static void ValidateDescriptor(PodcastLongFormContentDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            if (descriptor.ProtocolVersion != 1 ||
                string.IsNullOrWhiteSpace(descriptor.ContentId) ||
                descriptor.Revision < 0 ||
                descriptor.BlockCount <= 0 ||
                descriptor.CharacterCount <= 0)
            {
                throw new InvalidDataException("Unsupported IKUN long-form content descriptor.");
            }
        }

        private static void ValidateDocument(
            PodcastLongFormContentDocument document,
            PodcastLongFormContentDescriptor descriptor)
        {
            ValidateDescriptor(document);
            if (document.ProtocolVersion != descriptor.ProtocolVersion ||
                !string.Equals(document.ContentId, descriptor.ContentId, StringComparison.Ordinal) ||
                document.Revision != descriptor.Revision ||
                document.BlockCount != descriptor.BlockCount ||
                document.CharacterCount != descriptor.CharacterCount ||
                string.IsNullOrWhiteSpace(document.Title) ||
                document.Blocks.Count != document.BlockCount ||
                document.Blocks.Sum(block => block.Text.Length) != document.CharacterCount)
            {
                throw new InvalidDataException("IKUN long-form content does not match its descriptor.");
            }

            HashSet<string> blockIds = new(StringComparer.Ordinal);
            foreach (var block in document.Blocks)
            {
                if (string.IsNullOrWhiteSpace(block.Id) ||
                    !blockIds.Add(block.Id) ||
                    !BlockKinds.Contains(block.Kind) ||
                    string.IsNullOrWhiteSpace(block.Text) ||
                    block.Level is < 1 or > 6)
                {
                    throw new InvalidDataException("IKUN returned an invalid long-form content block.");
                }
            }

            ValidateContentUrl(document.OriginalUrl);
            ValidateContentUrl(document.AudioUrl);
            ValidateContentUrl(document.ShareUrl);
            var expectedShareUrl = document.OriginalUrl ?? document.AudioUrl;
            if (!string.Equals(document.ShareUrl, expectedShareUrl, StringComparison.Ordinal))
            {
                throw new InvalidDataException("IKUN returned an inconsistent long-form content share URL.");
            }
        }

        private static void ValidateContentUrl(string? value)
        {
            if (value == null) return;
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                !string.IsNullOrEmpty(uri.UserInfo))
            {
                throw new InvalidDataException("IKUN returned an invalid long-form content URL.");
            }
        }

        private static string FormatBlockText(PodcastLongFormContentBlock block) => block.Kind switch
        {
            "list-item" => $"- {block.Text}",
            "quote" => $"> {block.Text}",
            _ => block.Text,
        };

        private void ThrowIfSuperseded(
            long generation,
            PodcastLongFormContentDescriptor descriptor,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_generation != generation ||
                !string.Equals(_requestedContentId, descriptor.ContentId, StringComparison.Ordinal) ||
                _requestedRevision != descriptor.Revision)
            {
                throw new OperationCanceledException(
                    "Podcast long-form content request was superseded by newer content.",
                    cancellationToken);
            }
        }
    }
}
