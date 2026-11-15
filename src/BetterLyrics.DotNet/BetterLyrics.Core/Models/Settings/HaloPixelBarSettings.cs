using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterLyrics.Core.Models.Settings;

public partial class HaloPixelBarSettings : ObservableObject
{
    [ObservableProperty] public partial bool Enable { get; set; }
    [ObservableProperty] public partial int LyricMode { get; set; }
    [ObservableProperty] public partial bool AutoScroll { get; set; } = true;
    [ObservableProperty] public partial int ScrollThreshold { get; set; } = 30;
    [ObservableProperty] public partial bool Typewriter { get; set; }
    [ObservableProperty] public partial int TypewriterSpeed { get; set; } = 100;
    [ObservableProperty] public partial bool TypewriterSync { get; set; } = true;
    [ObservableProperty] public partial int LatencyCompMs { get; set; } = 100;
    [ObservableProperty] public partial int TextAlignment { get; set; }
    [ObservableProperty] public partial bool AlternateSplit { get; set; }
    [ObservableProperty] public partial int AlternateInterval { get; set; } = 3000;
}
