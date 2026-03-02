using System;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Media;

namespace Speakly.Helpers
{
    internal static class EditableModelComboHelper
    {
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
            if (!combo.IsDropDownOpen)
            {
                combo.IsDropDownOpen = true;
            }

            combo.Dispatcher.BeginInvoke(() =>
            {
                if (combo.Template.FindName("PART_Popup", combo) is not Popup popup) return;
                if (popup.Child is not DependencyObject root) return;

                var scrollViewer = FindChild<ScrollViewer>(root);
                scrollViewer?.ScrollToTop();
            }, DispatcherPriority.Background);
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
    }
}
