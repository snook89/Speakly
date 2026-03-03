using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using Hardcodet.Wpf.TaskbarNotification;
using Speakly.Config;

namespace Speakly.Services
{
    public class TrayIconService : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private TaskbarIcon _notifyIcon;
        private Window _mainWindow;
        private ContextMenu _contextMenu;

        public TrayIconService(Window mainWindow)
        {
            _mainWindow = mainWindow;
            
            // Create the NotifyIcon (TaskbarIcon)
            _notifyIcon = new TaskbarIcon
            {
                IconSource = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Resources/Speakly_new_logo.ico")),
                ToolTipText = "Speakly"
            };

            // Handle Double Click to restore window
            _notifyIcon.TrayMouseDoubleClick += OnTrayDoubleClick;

            // Create Context Menu
            _contextMenu = new ContextMenu
            {
                Placement = PlacementMode.MousePoint,
                StaysOpen = false
            };
            
            MenuItem settingsItem = new MenuItem { Header = "Settings" };
            settingsItem.Click += OnSettingsClick;

            MenuItem showOverlayItem = new MenuItem { Header = "Show Overlay" };
            showOverlayItem.Click += OnShowOverlayClick;

            MenuItem toggleRefinement = new MenuItem { Header = "Toggle Refinement" };
            toggleRefinement.Click += (_, _) => App.ViewModel.ToggleRefinementQuickCommand.Execute(null);

            MenuItem nextProfile = new MenuItem { Header = "Next Profile" };
            nextProfile.Click += (_, _) => App.ViewModel.CycleProfileCommand.Execute(null);

            var profilesMenu = new MenuItem { Header = "Profiles" };
            foreach (var profile in App.ViewModel.Profiles)
            {
                var item = new MenuItem { Header = profile.Name, Tag = profile.Id, IsCheckable = true };
                item.Click += (_, _) =>
                {
                    App.ViewModel.SetProfileByIdCommand.Execute(item.Tag?.ToString());
                    RefreshProfileChecks(profilesMenu);
                };
                profilesMenu.Items.Add(item);
            }
            RefreshProfileChecks(profilesMenu);
            
            MenuItem exitItem = new MenuItem { Header = "Exit" };
            exitItem.Click += OnExitClick;

            _contextMenu.Items.Add(settingsItem);
            _contextMenu.Items.Add(showOverlayItem);
            _contextMenu.Items.Add(toggleRefinement);
            _contextMenu.Items.Add(nextProfile);
            _contextMenu.Items.Add(profilesMenu);
            _contextMenu.Items.Add(new Separator());
            _contextMenu.Items.Add(exitItem);

            // Handle right-click manually so we can call SetForegroundWindow first.
            // This is required by Windows to ensure the menu anchors near the cursor
            // and dismisses correctly when the user clicks elsewhere.
            _notifyIcon.TrayRightMouseUp += OnTrayRightMouseUp;
            
            // Hook up minimized event from main window
            _mainWindow.StateChanged += OnWindowStateChanged;
            _mainWindow.Closing += OnWindowClosing;
        }

        private void OnTrayRightMouseUp(object sender, RoutedEventArgs e)
        {
            // SetForegroundWindow is required before showing a popup from the tray;
            // without it Windows doesn't know which window owns the menu and
            // positions it in the screen corner instead of near the cursor.
            var hwnd = new WindowInteropHelper(_mainWindow).Handle;
            SetForegroundWindow(hwnd);
            _contextMenu.IsOpen = true;
        }
        
        private void OnWindowStateChanged(object? sender, EventArgs e)
        {
            if (_mainWindow.WindowState == WindowState.Minimized && ConfigManager.Config.MinimizeToTray)
            {
                _mainWindow.Hide();
            }
        }

        private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (ConfigManager.Config.MinimizeToTray)
            {
                e.Cancel = true; // prevent close
                _mainWindow.Hide();
            }
            else
            {
                // Force shutdown because the overlay window is technically still alive!
                Application.Current.Shutdown();
            }
        }

        private void OnTrayDoubleClick(object sender, RoutedEventArgs e)
        {
            ShowMainWindow();
        }

        private void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            ShowMainWindow();
        }

        private void OnShowOverlayClick(object sender, RoutedEventArgs e)
        {
            ConfigManager.Config.ShowOverlay = true;
            App.RecoverOverlayPosition();
        }

        private void ShowMainWindow()
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }

        private void OnExitClick(object sender, RoutedEventArgs e)
        {
            ConfigManager.Config.MinimizeToTray = false; // Bypass the closing intercept
            Application.Current.Shutdown();
        }

        public void Dispose()
        {
            _notifyIcon?.Dispose();
        }

        private static void RefreshProfileChecks(MenuItem profilesMenu)
        {
            foreach (var entry in profilesMenu.Items)
            {
                if (entry is not MenuItem profileItem) continue;
                var id = profileItem.Tag?.ToString() ?? string.Empty;
                profileItem.IsChecked = string.Equals(id, App.ViewModel.SelectedProfile?.Id, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
