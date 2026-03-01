using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using Speakly.Config;

namespace Speakly.Services
{
    public class TrayIconService : IDisposable
    {
        private TaskbarIcon _notifyIcon;
        private Window _mainWindow;

        public TrayIconService(Window mainWindow)
        {
            _mainWindow = mainWindow;
            
            // Create the NotifyIcon (TaskbarIcon)
            _notifyIcon = new TaskbarIcon
            {
                IconSource = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Resources/speakly_HQ.ico")),
                ToolTipText = "Speakly"
            };

            // Handle Double Click to restore window
            _notifyIcon.TrayMouseDoubleClick += OnTrayDoubleClick;

            // Create Context Menu
            ContextMenu contextMenu = new ContextMenu();
            
            MenuItem settingsItem = new MenuItem { Header = "Settings" };
            settingsItem.Click += OnSettingsClick;
            
            MenuItem exitItem = new MenuItem { Header = "Exit" };
            exitItem.Click += OnExitClick;

            contextMenu.Items.Add(settingsItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(exitItem);

            _notifyIcon.ContextMenu = contextMenu;
            
            // Hook up minimized event from main window
            _mainWindow.StateChanged += OnWindowStateChanged;
            _mainWindow.Closing += OnWindowClosing;
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
    }
}
