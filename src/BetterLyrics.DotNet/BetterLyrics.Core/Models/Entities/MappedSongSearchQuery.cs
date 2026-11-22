using BetterLyrics.Core.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using LiteDB;

namespace BetterLyrics.Core.Models.Entities;

public partial class MappedSongSearchQuery : ObservableObject, ICloneable
{
    public ObjectId Id { get; set; }

    [ObservableProperty]
    public partial string OriginalTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OriginalArtist { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OriginalAlbum { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MappedTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MappedArtist { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MappedAlbum { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsMarkedAsPureMusic { get; set; } = false;

    [ObservableProperty]
    public partial LyricsProvider? LyricsSearchProvider { get; set; }

    // 保存实际选中的歌词，避免断网或同步失败后又自动选回另一个搜索结果。
    public LyricsCacheItem? ManualLyrics { get; set; }
    public bool MyGoOverrideSaved { get; set; }

    public object Clone()
    {
        return new MappedSongSearchQuery
        {
            OriginalTitle = OriginalTitle,
            OriginalArtist = OriginalArtist,
            OriginalAlbum = OriginalAlbum,
            MappedTitle = MappedTitle,
            MappedArtist = MappedArtist,
            MappedAlbum = MappedAlbum,
            IsMarkedAsPureMusic = IsMarkedAsPureMusic,
            LyricsSearchProvider = LyricsSearchProvider,
            ManualLyrics = (LyricsCacheItem?)ManualLyrics?.Clone(),
            MyGoOverrideSaved = MyGoOverrideSaved
        };
    }

    public override string ToString()
    {
        return
            $"Title: {OriginalTitle} -> {MappedTitle} " +
            $"Artist: {OriginalArtist} -> {MappedArtist} " +
            $"Album: {OriginalAlbum} -> {MappedAlbum}";
    }
}
