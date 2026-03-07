using System;
using System.Threading;
using System.Windows;
using Speakly.Config;

namespace Speakly.Services
{
    public sealed class RefinementContextSnapshot
    {
        public static RefinementContextSnapshot Empty { get; } = new();

        public string SelectedText { get; init; } = string.Empty;
        public string ClipboardText { get; init; } = string.Empty;

        public bool HasSelectedText => !string.IsNullOrWhiteSpace(SelectedText);
        public bool HasClipboardText => !string.IsNullOrWhiteSpace(ClipboardText);
        public bool HasAny => HasSelectedText || HasClipboardText;
    }

    public static class RefinementContextCaptureService
    {
        private const int MaxContextChars = 600;

        public static RefinementContextSnapshot Capture(AppConfig config, TargetWindowContext targetContext)
        {
            if (config == null)
            {
                return RefinementContextSnapshot.Empty;
            }

            bool includeSelectedText = config.UseSelectedTextContextForRefinement;
            bool includeClipboardText = config.UseClipboardContextForRefinement;
            if (!includeSelectedText && !includeClipboardText)
            {
                return RefinementContextSnapshot.Empty;
            }

            string clipboardText = string.Empty;
            if (includeClipboardText && TryReadClipboardText(out var currentClipboardText))
            {
                clipboardText = NormalizeContextText(currentClipboardText);
            }

            string selectedText = string.Empty;
            if (includeSelectedText)
            {
                if (TextInserter.TryCaptureSelectedText(targetContext, out var capturedSelectedText, out var errorCode))
                {
                    selectedText = NormalizeContextText(capturedSelectedText);
                }
                else if (!string.IsNullOrWhiteSpace(errorCode))
                {
                    Logger.Log($"Selected-text context capture skipped ({errorCode}).");
                }
            }

            if (includeClipboardText &&
                !string.IsNullOrWhiteSpace(selectedText) &&
                string.Equals(selectedText, clipboardText, StringComparison.Ordinal))
            {
                clipboardText = string.Empty;
            }

            return new RefinementContextSnapshot
            {
                SelectedText = includeSelectedText ? selectedText : string.Empty,
                ClipboardText = includeClipboardText ? clipboardText : string.Empty
            };
        }

        private static bool TryReadClipboardText(out string text)
        {
            text = string.Empty;

            for (int attempt = 1; attempt <= 6; attempt++)
            {
                try
                {
                    string? captured = null;
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (Clipboard.ContainsText())
                        {
                            captured = Clipboard.GetText();
                        }
                    });

                    text = captured?.Trim() ?? string.Empty;
                    return !string.IsNullOrWhiteSpace(text);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Clipboard context read retry {attempt} failed ({ex.GetType().Name}).");
                    Thread.Sleep(35);
                }
            }

            return false;
        }

        private static string NormalizeContextText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var normalized = text.Replace("\r\n", "\n").Trim();
            if (normalized.Length <= MaxContextChars)
            {
                return normalized;
            }

            return normalized[..MaxContextChars].TrimEnd() + "...";
        }
    }
}
