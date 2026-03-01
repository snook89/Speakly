using System.Windows;
using System.Windows.Media.Animation;
using Speakly.Config;

namespace Speakly
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = App.ViewModel;

            App.ViewModel.SaveSucceeded += FlashSaveGlow;

            Loaded  += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void FlashSaveGlow()
        {
            var sb = (Storyboard)FindResource("SaveGlowStoryboard");
            sb.Begin();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Restore window bounds
            if (!double.IsNaN(ConfigManager.Config.MainWindowLeft))   Left   = ConfigManager.Config.MainWindowLeft;
            if (!double.IsNaN(ConfigManager.Config.MainWindowTop))    Top    = ConfigManager.Config.MainWindowTop;
            if (!double.IsNaN(ConfigManager.Config.MainWindowWidth))  Width  = ConfigManager.Config.MainWindowWidth;
            if (!double.IsNaN(ConfigManager.Config.MainWindowHeight)) Height = ConfigManager.Config.MainWindowHeight;

            // Navigate to the first page
            RootNavigation.Navigate(typeof(Pages.GeneralPage));
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            ConfigManager.Config.MainWindowLeft   = Left;
            ConfigManager.Config.MainWindowTop    = Top;
            ConfigManager.Config.MainWindowWidth  = Width;
            ConfigManager.Config.MainWindowHeight = Height;
            ConfigManager.Save();
        }
    }
}
