using System.Text.Json.Serialization;

namespace BetterLyrics.Core.Models.Podcast
{
    public sealed class PodcastTranscriptDescriptor
    {
        [JsonPropertyName("protocolVersion")] public int ProtocolVersion { get; set; }
        [JsonPropertyName("contentId")] public string ContentId { get; set; } = "";
        [JsonPropertyName("revision")] public int Revision { get; set; }
        [JsonPropertyName("state")] public string State { get; set; } = "";
        [JsonPropertyName("isPartial")] public bool IsPartial { get; set; }
    }

    public sealed class PodcastTranscriptDelta
    {
        [JsonPropertyName("protocolVersion")] public int ProtocolVersion { get; set; }
        [JsonPropertyName("contentId")] public string ContentId { get; set; } = "";
        [JsonPropertyName("baseRevision")] public int BaseRevision { get; set; }
        [JsonPropertyName("revision")] public int Revision { get; set; }
        [JsonPropertyName("reset")] public bool Reset { get; set; }
        [JsonPropertyName("state")] public string State { get; set; } = "";
        [JsonPropertyName("isPartial")] public bool IsPartial { get; set; }
        [JsonPropertyName("upsertLines")] public List<PodcastTranscriptLine> UpsertLines { get; set; } = [];
        [JsonPropertyName("deletedLineIds")] public List<string> DeletedLineIds { get; set; } = [];
        [JsonPropertyName("speakers")] public List<PodcastTranscriptSpeaker> Speakers { get; set; } = [];
    }

    public sealed class PodcastTranscriptLine
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("startMs")] public int StartMs { get; set; }
        [JsonPropertyName("endMs")] public int EndMs { get; set; }
        [JsonPropertyName("displayText")] public string DisplayText { get; set; } = "";
        [JsonPropertyName("speakerId")] public string? SpeakerId { get; set; }
        [JsonPropertyName("words")] public List<PodcastTranscriptWord> Words { get; set; } = [];
    }

    public sealed class PodcastTranscriptWord
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("startIndex")] public int StartIndex { get; set; }
        [JsonPropertyName("length")] public int Length { get; set; }
        [JsonPropertyName("startMs")] public int StartMs { get; set; }
        [JsonPropertyName("endMs")] public int EndMs { get; set; }
    }

    public sealed class PodcastTranscriptSpeaker
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
    }
}
