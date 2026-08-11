using BetterLyrics.Core.Models.Lyrics;
using BetterLyrics.Core.Models.Podcast;
using BetterLyrics.Core.Serialization;
using System.Net.Http.Json;

namespace BetterLyrics.Core.Implementations.Services
{
    public sealed class PodcastTranscriptBridge
    {
        private static readonly HashSet<string> States =
            ["missing", "preparing", "ready", "failed", "unavailable"];
        private static readonly HttpClient SharedHttpClient = new() { Timeout = TimeSpan.FromSeconds(3) };
        private readonly object _sync = new();
        private readonly HttpClient _httpClient;
        private readonly Dictionary<string, PodcastTranscriptLine> _lines = [];
        private readonly Dictionary<string, PodcastTranscriptSpeaker> _speakers = [];
        private string _requestedContentId = "";
        private long _generation;

        public PodcastTranscriptBridge(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? SharedHttpClient;
        }

        public string ContentId { get; private set; } = "";
        public int Revision { get; private set; }
        public string State { get; private set; } = "missing";
        public bool IsPartial { get; private set; }
        public bool HasLines
        {
            get
            {
                lock (_sync) return _lines.Count > 0;
            }
        }

        public void Reset()
        {
            lock (_sync) ResetCore();
        }

        private void ResetCore()
        {
            _generation++;
            _requestedContentId = "";
            ContentId = "";
            Revision = 0;
            State = "missing";
            IsPartial = false;
            _lines.Clear();
            _speakers.Clear();
        }

        public async Task<LyricsData> UpdateAsync(
            string serverAddress,
            PodcastTranscriptDescriptor descriptor,
            int currentPositionMs,
            CancellationToken cancellationToken = default)
        {
            ValidateDescriptor(descriptor);
            long generation;
            int sinceRevision;
            bool shouldFetch;
            lock (_sync)
            {
                if (!string.Equals(_requestedContentId, descriptor.ContentId, StringComparison.Ordinal) ||
                    descriptor.Revision < Revision)
                {
                    ResetCore();
                    _requestedContentId = descriptor.ContentId;
                }

                generation = _generation;
                sinceRevision = Revision;
                shouldFetch = descriptor.Revision != Revision || ContentId.Length == 0;
            }

            if (shouldFetch)
            {
                try
                {
                    ApplyForRequest(
                        await FetchDeltaAsync(
                            serverAddress,
                            descriptor.ContentId,
                            sinceRevision,
                            cancellationToken),
                        generation,
                        descriptor.ContentId,
                        cancellationToken);
                }
                catch (TranscriptRevisionException)
                {
                    var reset = await FetchDeltaAsync(serverAddress, descriptor.ContentId, 0, cancellationToken);
                    if (!reset.Reset)
                    {
                        throw new InvalidDataException("IKUN did not return a reset after a revision gap.");
                    }
                    ApplyForRequest(
                        reset,
                        generation,
                        descriptor.ContentId,
                        cancellationToken);
                }
            }

            lock (_sync)
            {
                ThrowIfSuperseded(generation, descriptor.ContentId, cancellationToken);
                State = descriptor.State;
                IsPartial = descriptor.IsPartial;
                return ToLyricsDataCore(currentPositionMs);
            }
        }

        public void Apply(PodcastTranscriptDelta delta)
        {
            lock (_sync)
            {
                _requestedContentId = delta.ContentId;
                ApplyCore(delta);
            }
        }

        private void ApplyForRequest(
            PodcastTranscriptDelta delta,
            long generation,
            string contentId,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                ThrowIfSuperseded(generation, contentId, cancellationToken);
                ApplyCore(delta);
            }
        }

        private void ApplyCore(PodcastTranscriptDelta delta)
        {
            ValidateDelta(delta);
            if (!delta.Reset && ContentId.Length > 0 &&
                !string.Equals(ContentId, delta.ContentId, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Transcript content changed without a reset.");
            }
            if (!delta.Reset && Revision != delta.BaseRevision)
            {
                throw new TranscriptRevisionException();
            }

            var nextLines = delta.Reset
                ? new Dictionary<string, PodcastTranscriptLine>()
                : new Dictionary<string, PodcastTranscriptLine>(_lines, StringComparer.Ordinal);
            var nextSpeakers = delta.Reset
                ? new Dictionary<string, PodcastTranscriptSpeaker>()
                : new Dictionary<string, PodcastTranscriptSpeaker>(_speakers, StringComparer.Ordinal);

            foreach (var speaker in delta.Speakers) nextSpeakers[speaker.Id] = speaker;
            foreach (var id in delta.DeletedLineIds) nextLines.Remove(id);
            foreach (var line in delta.UpsertLines) nextLines[line.Id] = line;

            _lines.Clear();
            _speakers.Clear();
            foreach (var pair in nextLines) _lines[pair.Key] = pair.Value;
            foreach (var pair in nextSpeakers) _speakers[pair.Key] = pair.Value;
            ContentId = delta.ContentId;
            Revision = delta.Revision;
            State = delta.State;
            IsPartial = delta.IsPartial;
        }

        public LyricsData ToLyricsData(int currentPositionMs = 0)
        {
            lock (_sync) return ToLyricsDataCore(currentPositionMs);
        }

        private LyricsData ToLyricsDataCore(int currentPositionMs)
        {
            var lines = _lines.Values
                .OrderBy(line => line.StartMs)
                .ThenBy(line => line.Id, StringComparer.Ordinal)
                .Select(ToLyricsLine)
                .ToList();

            if (lines.Count == 0)
            {
                return StatusLyrics(State switch
                {
                    "preparing" => "正在生成字幕",
                    "failed" => "字幕生成失败，请在 IKUN 中重试",
                    "unavailable" => "当前内容不支持字幕",
                    _ => "暂无字幕，请在 IKUN 中生成"
                });
            }

            return new LyricsData(lines) { AutoGenerated = true };
        }

        public static LyricsData WaitingForConnection() => StatusLyrics("等待字幕连接");

        private async Task<PodcastTranscriptDelta> FetchDeltaAsync(
            string serverAddress,
            string contentId,
            int sinceRevision,
            CancellationToken cancellationToken)
        {
            var baseUri = new Uri(serverAddress.TrimEnd('/') + "/", UriKind.Absolute);
            var uri = new Uri(
                baseUri,
                $"transcript?contentId={Uri.EscapeDataString(contentId)}&sinceRevision={sinceRevision}");
            return await _httpClient.GetFromJsonAsync(
                uri,
                SourceGenerationContext.Default.PodcastTranscriptDelta,
                cancellationToken) ?? throw new InvalidDataException("IKUN returned an empty transcript response.");
        }

        private static void ValidateDescriptor(PodcastTranscriptDescriptor descriptor)
        {
            if (descriptor.ProtocolVersion != 2 ||
                string.IsNullOrWhiteSpace(descriptor.ContentId) ||
                descriptor.Revision < 0 ||
                !States.Contains(descriptor.State))
            {
                throw new InvalidDataException("Unsupported IKUN transcript descriptor.");
            }
        }

        private static void ValidateDelta(PodcastTranscriptDelta delta)
        {
            if (delta.ProtocolVersion != 2 ||
                string.IsNullOrWhiteSpace(delta.ContentId) ||
                delta.BaseRevision < 0 ||
                delta.Revision < delta.BaseRevision ||
                !States.Contains(delta.State))
            {
                throw new InvalidDataException("Unsupported IKUN transcript response.");
            }
            if (delta.DeletedLineIds.Any(string.IsNullOrWhiteSpace) ||
                delta.Speakers.Any(speaker =>
                    string.IsNullOrWhiteSpace(speaker.Id) || string.IsNullOrWhiteSpace(speaker.Name)))
            {
                throw new InvalidDataException("IKUN returned invalid transcript metadata.");
            }
            foreach (var line in delta.UpsertLines) ValidateLine(line);
        }

        private static void ValidateLine(PodcastTranscriptLine line)
        {
            if (string.IsNullOrWhiteSpace(line.Id) || line.StartMs < 0 ||
                line.EndMs <= line.StartMs || string.IsNullOrEmpty(line.DisplayText))
            {
                throw new InvalidDataException("IKUN returned an invalid transcript line.");
            }

            var previousIndex = 0;
            var previousStartMs = line.StartMs;
            foreach (var word in line.Words)
            {
                var endIndex = (long)word.StartIndex + word.Length;
                if (string.IsNullOrWhiteSpace(word.Id) || word.StartIndex < previousIndex ||
                    word.Length <= 0 || endIndex > line.DisplayText.Length ||
                    word.StartMs < previousStartMs || word.EndMs <= word.StartMs ||
                    word.StartMs < line.StartMs || word.EndMs > line.EndMs)
                {
                    throw new InvalidDataException("IKUN returned invalid word timing or character indexes.");
                }
                previousIndex = (int)endIndex;
                previousStartMs = word.StartMs;
            }
        }

        private void ThrowIfSuperseded(
            long generation,
            string contentId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_generation != generation ||
                !string.Equals(_requestedContentId, contentId, StringComparison.Ordinal))
            {
                throw new OperationCanceledException(
                    "Podcast transcript request was superseded by newer content.",
                    cancellationToken);
            }
        }

        private LyricsLine ToLyricsLine(PodcastTranscriptLine source)
        {
            var speakerName = source.SpeakerId is not null && _speakers.TryGetValue(source.SpeakerId, out var speaker)
                ? speaker.Name
                : "";
            var prefix = speakerName.Length > 0 ? $"{speakerName}：" : "";
            var result = new LyricsLine
            {
                StartMs = source.StartMs,
                EndMs = source.EndMs,
                PrimaryText = prefix + source.DisplayText,
                AgentId = source.SpeakerId ?? "",
                IsPrimaryHasRealSyllableInfo = source.Words.Count > 0
            };

            if (source.Words.Count == 0)
            {
                result.PrimarySyllables.Add(new BaseLyrics
                {
                    StartMs = source.StartMs,
                    EndMs = source.EndMs,
                    Text = result.PrimaryText,
                    StartIndex = 0
                });
            }
            else if (prefix.Length > 0)
            {
                var firstWordStartMs = source.Words[0].StartMs;
                result.PrimarySyllables.Add(new BaseLyrics
                {
                    StartMs = source.StartMs,
                    EndMs = Math.Max(source.StartMs + 1, firstWordStartMs),
                    Text = prefix,
                    StartIndex = 0
                });
            }

            foreach (var word in source.Words)
            {
                result.PrimarySyllables.Add(new BaseLyrics
                {
                    StartMs = word.StartMs,
                    EndMs = word.EndMs,
                    Text = source.DisplayText.Substring(word.StartIndex, word.Length),
                    StartIndex = prefix.Length + word.StartIndex
                });
            }
            result.RebuildPrimaryChars();
            return result;
        }

        private static LyricsData StatusLyrics(string text) =>
            new([StatusLine(text, 0, (int)TimeSpan.FromHours(24).TotalMilliseconds)])
            {
                AutoGenerated = true,
                LanguageCode = "N/A"
            };

        private static LyricsLine StatusLine(string text, int startMs, int endMs)
        {
            var line = new LyricsLine
            {
                StartMs = startMs,
                EndMs = endMs,
                PrimaryText = text,
                IsShareable = false,
                IsPrimaryHasRealSyllableInfo = false,
                PrimarySyllables =
                [
                    new BaseLyrics
                    {
                        StartMs = startMs,
                        EndMs = endMs,
                        Text = text,
                        StartIndex = 0
                    }
                ]
            };
            line.RebuildPrimaryChars();
            return line;
        }

        private sealed class TranscriptRevisionException : Exception { }
    }
}
