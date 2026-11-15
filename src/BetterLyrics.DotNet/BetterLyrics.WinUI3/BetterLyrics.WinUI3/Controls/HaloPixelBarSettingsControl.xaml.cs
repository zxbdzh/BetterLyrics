using BetterLyrics.WinUI3.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace BetterLyrics.WinUI3.Controls;

public sealed partial class HaloPixelBarSettingsControl : BaseUserControl
{
    public HaloPixelBarSettingsControlViewModel ViewModel =>
        (HaloPixelBarSettingsControlViewModel)DataContext;

    public HaloPixelBarSettingsControl()
    {
        InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<HaloPixelBarSettingsControlViewModel>();
    }

    protected override void BindGeneratedControls() => Bindings?.Update();
    protected override void UnbindGeneratedControls() => Bindings?.StopTracking();
    protected override void OnLoaded() => ViewModel.StartStatusPolling();
    protected override void OnUnloaded() => ViewModel.StopStatusPolling();
}
