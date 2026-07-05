using BetterLyrics.WinUI3.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace BetterLyrics.WinUI3.Controls
{
    public sealed partial class HaloPixelBarSettingsControl : UserControl
    {
        public HaloPixelBarSettingsControlViewModel ViewModel => (HaloPixelBarSettingsControlViewModel)DataContext;

        public HaloPixelBarSettingsControl()
        {
            InitializeComponent();
            DataContext = Ioc.Default.GetRequiredService<HaloPixelBarSettingsControlViewModel>();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            ViewModel.StartStatusPolling();
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            ViewModel.StopStatusPolling();
        }
    }
}
