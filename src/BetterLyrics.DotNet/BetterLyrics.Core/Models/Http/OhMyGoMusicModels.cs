using System.Text.Json.Serialization;

namespace BetterLyrics.Core.Models.Http;

public sealed class OhMyGoMusicStatus
{
    [JsonPropertyName("trackId")] public ulong TrackId { get; init; }
    [JsonPropertyName("title")] public string Title { get; init; } = string.Empty;
    [JsonPropertyName("artist")] public string Artist { get; init; } = string.Empty;
    [JsonPropertyName("album")] public string Album { get; init; } = string.Empty;
    [JsonPropertyName("playing")] public bool Playing { get; init; }
    [JsonPropertyName("positionMs")] public long PositionMs { get; init; }
    [JsonPropertyName("durationMs")] public long DurationMs { get; init; }
    [JsonPropertyName("lyricsReady")] public bool LyricsReady { get; init; }
}

public sealed class OhMyGoMusicLyrics
{
    [JsonPropertyName("trackId")] public ulong TrackId { get; init; }
    // Go 会省略空字段；普通 setter 可让 JSON 源生成器保留缺失字段的默认值。
    [JsonPropertyName("lrc")] public string Lrc { get; set; } = string.Empty;
    [JsonPropertyName("translation")] public string Translation { get; set; } = string.Empty;
    [JsonPropertyName("lines")] public List<OhMyGoMusicLyricLine> Lines { get; set; } = [];
}

public sealed class OhMyGoMusicLyricLine
{
    [JsonPropertyName("start")] public int Start { get; init; }
    [JsonPropertyName("end")] public int End { get; init; }
    [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
    [JsonPropertyName("translation")] public string Translation { get; set; } = string.Empty;
    [JsonPropertyName("words")] public List<OhMyGoMusicLyricWord> Words { get; set; } = [];
}

public sealed class OhMyGoMusicLyricWord
{
    [JsonPropertyName("start")] public int Start { get; init; }
    [JsonPropertyName("end")] public int End { get; init; }
    [JsonPropertyName("text")] public string Text { get; init; } = string.Empty;
}
