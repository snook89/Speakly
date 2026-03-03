using System.Windows;
using Speakly.Config;

namespace Speakly
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = App.ViewModel;

            Loaded  += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Restore window bounds
            if (!double.IsNaN(ConfigManager.Config.MainWindowLeft))   Left   = ConfigManager.Config.MainWindowLeft;
            if (!double.IsNaN(ConfigManager.Config.MainWindowTop))    Top    = ConfigManager.Config.MainWindowTop;
            if (!double.IsNaN(ConfigManager.Config.MainWindowWidth))  Width  = ConfigManager.Config.MainWindowWidth;
            if (!double.IsNaN(ConfigManager.Config.MainWindowHeight)) Height = ConfigManager.Config.MainWindowHeight;

            // Navigate to the first page
            RootNavigation.Navigate(typeof(Pages.HomePage));
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
