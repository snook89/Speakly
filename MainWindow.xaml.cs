using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Speakly.Config;

namespace Speakly
{
    public partial class MainWindow : Window
    {
        private const double SidebarExpandedWidth = 220;
        private const double SidebarCollapsedWidth = 56;

        private readonly Dictionary<string, Type> _pages = new()
        {
            ["Home"] = typeof(Pages.HomePage),
            ["General"] = typeof(Pages.GeneralPage),
            ["Hotkeys"] = typeof(Pages.HotkeysPage),
            ["Audio"] = typeof(Pages.AudioPage),
            ["Transcription"] = typeof(Pages.TranscriptionPage),
            ["Refinement"] = typeof(Pages.RefinementPage),
            ["API Keys"] = typeof(Pages.ApiKeysPage),
            ["History"] = typeof(Pages.HistoryPage),
            ["Statistics"] = typeof(Pages.StatisticsPage),
            ["Info"] = typeof(Pages.InfoPage)
        };

        private bool _syncingSelection;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = App.ViewModel;

            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (!double.IsNaN(ConfigManager.Config.MainWindowLeft)) Left = ConfigManager.Config.MainWindowLeft;
            if (!double.IsNaN(ConfigManager.Config.MainWindowTop)) Top = ConfigManager.Config.MainWindowTop;
            if (!double.IsNaN(ConfigManager.Config.MainWindowWidth)) Width = ConfigManager.Config.MainWindowWidth;
            if (!double.IsNaN(ConfigManager.Config.MainWindowHeight)) Height = ConfigManager.Config.MainWindowHeight;

            SidebarToggle.IsChecked = true;
            ApplySidebarState();
            NavHome.IsSelected = true;
            NavigateTo("Home");
        }

        private void SidebarToggle_Changed(object sender, RoutedEventArgs e)
        {
            ApplySidebarState();
        }

        private void ApplySidebarState()
        {
            SidebarHost.Width = SidebarToggle.IsChecked == true
                ? SidebarExpandedWidth
                : SidebarCollapsedWidth;
        }

        private void NavigationList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncingSelection)
            {
                return;
            }

            if (sender is not ListBox list || list.SelectedItem is not ListBoxItem item || item.Tag is not string section)
            {
                return;
            }

            _syncingSelection = true;
            try
            {
                if (ReferenceEquals(list, MainNavList))
                {
                    FooterNavList.SelectedItem = null;
                }
                else
                {
                    MainNavList.SelectedItem = null;
                }

                NavigateTo(section);
            }
            finally
            {
                _syncingSelection = false;
            }
        }

        private void SidebarNavItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not ListBoxItem item)
            {
                return;
            }

            var parentList = FindAncestor<ListBox>(item);
            if (parentList == null)
            {
                return;
            }

            parentList.SelectedItem = item;
            e.Handled = false;
        }

        private void NavigateTo(string section)
        {
            if (!_pages.TryGetValue(section, out var pageType))
            {
                return;
            }

            if (ContentHost.Content?.GetType() != pageType)
            {
                if (Activator.CreateInstance(pageType) is FrameworkElement view)
                {
                    ContentHost.Content = view;
                }
            }

            Title = $"Speakly - {section}";
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            ConfigManager.Config.MainWindowLeft = Left;
            ConfigManager.Config.MainWindowTop = Top;
            ConfigManager.Config.MainWindowWidth = Width;
            ConfigManager.Config.MainWindowHeight = Height;
            ConfigManager.Save();
        }

        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match)
                {
                    return match;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }
    }
}
