using System.Text.Json.Serialization;

namespace BetterLyrics.Core.Models.Podcast
{
    public class PodcastLongFormContentDescriptor
    {
        [JsonPropertyName("protocolVersion")] public int ProtocolVersion { get; set; }
        [JsonPropertyName("contentId")] public string ContentId { get; set; } = "";
        [JsonPropertyName("revision")] public long Revision { get; set; }
        [JsonPropertyName("blockCount")] public int BlockCount { get; set; }
        [JsonPropertyName("characterCount")] public int CharacterCount { get; set; }
    }

    public sealed class PodcastLongFormContentDocument : PodcastLongFormContentDescriptor
    {
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        [JsonPropertyName("blocks")] public List<PodcastLongFormContentBlock> Blocks { get; set; } = [];
        [JsonPropertyName("originalUrl")] public string? OriginalUrl { get; set; }
        [JsonPropertyName("audioUrl")] public string? AudioUrl { get; set; }
        [JsonPropertyName("shareUrl")] public string? ShareUrl { get; set; }
    }

    public sealed class PodcastLongFormContentBlock
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("kind")] public string Kind { get; set; } = "";
        [JsonPropertyName("text")] public string Text { get; set; } = "";
        [JsonPropertyName("level")] public int? Level { get; set; }
    }
}
