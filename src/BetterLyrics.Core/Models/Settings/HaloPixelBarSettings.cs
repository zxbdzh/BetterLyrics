using CommunityToolkit.Mvvm.ComponentModel;

namespace BetterLyrics.Core.Models.Settings
{
    public partial class HaloPixelBarSettings : ObservableRecipient
    {
        [ObservableProperty][NotifyPropertyChangedRecipients] public partial bool Enable { get; set; } = false;
        [ObservableProperty][NotifyPropertyChangedRecipients] public partial int LyricMode { get; set; } = 0;
        [ObservableProperty][NotifyPropertyChangedRecipients] public partial bool AutoScroll { get; set; } = true;
        [ObservableProperty][NotifyPropertyChangedRecipients] public partial int ScrollThreshold { get; set; } = 30;
        [ObservableProperty][NotifyPropertyChangedRecipients] public partial bool Typewriter { get; set; } = false;
        [ObservableProperty][NotifyPropertyChangedRecipients] public partial int TypewriterSpeed { get; set; } = 100;
        [ObservableProperty][NotifyPropertyChangedRecipients] public partial bool TypewriterSync { get; set; } = true;
        [ObservableProperty][NotifyPropertyChangedRecipients] public partial int LatencyCompMs { get; set; } = 100;
        [ObservableProperty][NotifyPropertyChangedRecipients] public partial int TextAlignment { get; set; } = 0; // 0=居中, 1=左对齐, 2=右对齐
        [ObservableProperty][NotifyPropertyChangedRecipients] public partial bool AlternateSplit { get; set; } = true;
        [ObservableProperty][NotifyPropertyChangedRecipients] public partial int AlternateInterval { get; set; } = 3000;
    }
}
