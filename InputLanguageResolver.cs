using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Speakly.Services
{
    public static class InputLanguageResolver
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetKeyboardLayout(uint idThread);

        public static string ResolveCurrentLanguageCode(string fallback = "en")
        {
            try
            {
                var foregroundWindow = GetForegroundWindow();
                if (foregroundWindow == IntPtr.Zero) return fallback;

                uint threadId = GetWindowThreadProcessId(foregroundWindow, out _);
                IntPtr keyboardLayout = GetKeyboardLayout(threadId);
                if (keyboardLayout == IntPtr.Zero) return fallback;

                int langId = (int)keyboardLayout & 0xFFFF;
                var culture = new CultureInfo(langId);
                var code = culture.TwoLetterISOLanguageName?.Trim().ToLowerInvariant();

                return string.IsNullOrWhiteSpace(code) ? fallback : code;
            }
            catch
            {
                return fallback;
            }
        }
    }
}
