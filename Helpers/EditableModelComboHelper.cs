using System;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Input;

namespace Speakly.Helpers
{
    internal static class EditableModelComboHelper
    {
        public static void HandleKeyUp(ComboBox combo, Key key)
        {
            if (!ShouldApplyFilterForKey(key))
            {
                return;
            }

            HandleTextChanged(combo);
        }

        public static void HandleTextChanged(ComboBox combo)
        {
            if (!combo.IsKeyboardFocusWithin) return;

            var query = combo.Text?.Trim() ?? string.Empty;
            ApplyContainsFilter(combo, query);

            if (!string.IsNullOrWhiteSpace(query) && !combo.IsDropDownOpen)
            {
                combo.IsDropDownOpen = true;
            }
        }

        public static void ResetFilter(ComboBox combo)
        {
            var view = CollectionViewSource.GetDefaultView(combo.ItemsSource ?? combo.Items);
            if (view == null || view.Filter == null) return;
            view.Filter = null;
            view.Refresh();
        }

        public static void ScrollDropDownToTop(ComboBox combo)
        {
            combo.ApplyTemplate();

            if (!combo.IsDropDownOpen)
            {
                combo.IsDropDownOpen = true;
            }

            combo.Dispatcher.BeginInvoke(() =>
            {
                var scrollViewer = FindDropDownScrollViewer(combo);
                if (scrollViewer != null)
                {
                    scrollViewer.ScrollToTop();
                    return;
                }

                // Fallback: force the first item into view when template internals differ.
                if (combo.Items.Count == 0) return;
                var firstItem = combo.Items[0];
                if (combo.ItemContainerGenerator.ContainerFromItem(firstItem) is FrameworkElement container)
                {
                    container.BringIntoView();
                }
            }, DispatcherPriority.ContextIdle);
        }

        private static void ApplyContainsFilter(ComboBox combo, string query)
        {
            var view = CollectionViewSource.GetDefaultView(combo.ItemsSource ?? combo.Items);
            if (view == null) return;

            if (string.IsNullOrWhiteSpace(query))
            {
                view.Filter = null;
            }
            else
            {
                view.Filter = item =>
                {
                    var text = item as string;
                    return text != null && text.Contains(query, StringComparison.OrdinalIgnoreCase);
                };
            }

            view.Refresh();
        }

        private static T? FindChild<T>(DependencyObject root) where T : DependencyObject
        {
            if (root is T match) return match;

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var found = FindChild<T>(child);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static ScrollViewer? FindDropDownScrollViewer(ComboBox combo)
        {
            if (TryGetPopup(combo) is Popup popup && popup.Child is DependencyObject popupRoot)
            {
                var popupScrollViewer = FindChild<ScrollViewer>(popupRoot);
                if (popupScrollViewer != null)
                {
                    return popupScrollViewer;
                }
            }

            return FindChild<ScrollViewer>(combo);
        }

        private static Popup? TryGetPopup(ComboBox combo)
        {
            if (combo.Template.FindName("PART_Popup", combo) is Popup standardPopup)
            {
                return standardPopup;
            }

            if (combo.Template.FindName("Popup", combo) is Popup customPopup)
            {
                return customPopup;
            }

            return null;
        }

        private static bool ShouldApplyFilterForKey(Key key)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ||
                Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ||
                Keyboard.Modifiers.HasFlag(ModifierKeys.Windows))
            {
                return false;
            }

            return key switch
            {
                Key.Up => false,
                Key.Down => false,
                Key.Left => false,
                Key.Right => false,
                Key.PageUp => false,
                Key.PageDown => false,
                Key.Home => false,
                Key.End => false,
                Key.Tab => false,
                Key.Enter => false,
                Key.Escape => false,
                Key.LeftShift => false,
                Key.RightShift => false,
                Key.LeftCtrl => false,
                Key.RightCtrl => false,
                Key.LeftAlt => false,
                Key.RightAlt => false,
                Key.LWin => false,
                Key.RWin => false,
                _ => true
            };
        }
    }
}
