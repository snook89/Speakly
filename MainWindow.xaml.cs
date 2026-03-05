using System;
using System.Windows;
using System.Windows.Threading;
using Speakly.Config;
using Wpf.Ui.Controls;

namespace Speakly
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = App.ViewModel;

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            RootNavigation.Navigated += RootNavigation_Navigated;
            RootNavigation.PaneOpened += RootNavigation_PaneVisibilityChanged;
            RootNavigation.PaneClosed += RootNavigation_PaneVisibilityChanged;
            RootNavigation.SizeChanged += (_, _) => UpdateSidebarBackdropWidthDeferred();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Restore window bounds
            if (!double.IsNaN(ConfigManager.Config.MainWindowLeft)) Left = ConfigManager.Config.MainWindowLeft;
            if (!double.IsNaN(ConfigManager.Config.MainWindowTop)) Top = ConfigManager.Config.MainWindowTop;
            if (!double.IsNaN(ConfigManager.Config.MainWindowWidth)) Width = ConfigManager.Config.MainWindowWidth;
            if (!double.IsNaN(ConfigManager.Config.MainWindowHeight)) Height = ConfigManager.Config.MainWindowHeight;

            // Navigate to the first page
            RootNavigation.Navigate(typeof(Pages.HomePage));
            UpdateSidebarBackdropWidthDeferred();
            UpdateWindowTitle();
        }

        private void RootNavigation_PaneVisibilityChanged(NavigationView sender, RoutedEventArgs args)
        {
            UpdateSidebarBackdropWidthDeferred();
        }

        private void RootNavigation_Navigated(NavigationView sender, NavigatedEventArgs args)
        {
            UpdateWindowTitle();
        }

        private void UpdateWindowTitle()
        {
            if (RootNavigation.SelectedItem is NavigationViewItem item &&
                item.Content is string section &&
                !string.IsNullOrWhiteSpace(section))
            {
                Title = $"Speakly - {section}";
                return;
            }

            Title = "Speakly";
        }

        private void UpdateSidebarBackdropWidthDeferred()
        {
            Dispatcher.BeginInvoke(
                UpdateSidebarBackdropWidth,
                DispatcherPriority.Loaded);
        }

        private void UpdateSidebarBackdropWidth()
        {
            if (SidebarPaneBackdrop is null || SidebarRightEdgeCover is null)
            {
                return;
            }

            var paneWidth = RootNavigation.IsPaneOpen
                ? RootNavigation.OpenPaneLength
                : RootNavigation.CompactPaneLength;

            SidebarPaneBackdrop.Width = paneWidth;

            SidebarRightEdgeCover.Margin = new Thickness(
                Math.Max(0, paneWidth - 2),
                0,
                0,
                0);
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            ConfigManager.Config.MainWindowLeft = Left;
            ConfigManager.Config.MainWindowTop = Top;
            ConfigManager.Config.MainWindowWidth = Width;
            ConfigManager.Config.MainWindowHeight = Height;
            ConfigManager.Save();
        }
    }
}
