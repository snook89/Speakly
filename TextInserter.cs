using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;

namespace Speakly.Services
{
    public enum InsertionFailureReason
    {
        None,
        MissingTarget,
        TargetWindowUnavailable,
        FocusRestoreFailed,
        ClipboardUnavailable,
        InputBlockedByIntegrity,
        Unknown
    }

    public readonly struct TargetWindowContext
    {
        public static TargetWindowContext Empty { get; } = new(IntPtr.Zero, 0, string.Empty, string.Empty, DateTime.MinValue);

        public TargetWindowContext(IntPtr handle, uint processId, string processName, string windowTitle, DateTime capturedAtUtc)
        {
            Handle = handle;
            ProcessId = processId;
            ProcessName = processName ?? string.Empty;
            WindowTitle = windowTitle ?? string.Empty;
            CapturedAtUtc = capturedAtUtc;
        }

        public IntPtr Handle { get; }
        public uint ProcessId { get; }
        public string ProcessName { get; }
        public string WindowTitle { get; }
        public DateTime CapturedAtUtc { get; }
        public bool IsValid => Handle != IntPtr.Zero;

        public override string ToString()
        {
            return $"hwnd={Handle}, pid={ProcessId}, process={ProcessName}, title='{WindowTitle}'";
        }
    }

    public class InsertResult
    {
        public bool Success { get; set; }
        public string Method { get; set; } = "None";
        public string ErrorCode { get; set; } = string.Empty;
        public InsertionFailureReason FailureReason { get; set; } = InsertionFailureReason.None;
        public bool ClipboardUpdated { get; set; }
        public bool TargetLocked { get; set; }
    }

    public static class TextInserter
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, [MarshalAs(UnmanagedType.LPArray), In] INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const uint ProcessQueryLimitedInformation = 0x1000;

        [Flags]
        public enum InputType : uint
        {
            Keyboard = 1
        }

        [Flags]
        public enum KeyEventFlags : uint
        {
            KeyDown = 0x0000,
            KeyUp = 0x0002,
            Unicode = 0x0004
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public static TargetWindowContext CaptureForegroundWindowContext()
        {
            var handle = GetForegroundWindow();
            if (handle == IntPtr.Zero)
            {
                return TargetWindowContext.Empty;
            }

            uint processId;
            GetWindowThreadProcessId(handle, out processId);

            string processName = string.Empty;
            if (processId != 0)
            {
                try
                {
                    processName = Process.GetProcessById((int)processId).ProcessName;
                }
                catch
                {
                    processName = string.Empty;
                }
            }

            return new TargetWindowContext(
                handle,
                processId,
                processName,
                GetWindowTitle(handle),
                DateTime.UtcNow);
        }

        public static InsertResult InsertText(string text, IntPtr targetWindow = default)
        {
            var context = targetWindow == IntPtr.Zero
                ? TargetWindowContext.Empty
                : BuildContextFromHandle(targetWindow);

            return InsertText(text, context);
        }

        public static InsertResult InsertText(string text, TargetWindowContext targetContext)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new InsertResult
                {
                    Success = true,
                    Method = "SkippedEmpty",
                    FailureReason = InsertionFailureReason.None,
                    TargetLocked = true
                };
            }

            if (!targetContext.IsValid)
            {
                return BuildClipboardOnlyFailure(
                    text,
                    "target_missing",
                    InsertionFailureReason.MissingTarget,
                    "Target window is missing.");
            }

            if (!IsWindow(targetContext.Handle))
            {
                return BuildClipboardOnlyFailure(
                    text,
                    "target_closed",
                    InsertionFailureReason.TargetWindowUnavailable,
                    $"Target window is no longer available ({targetContext}).");
            }

            bool likelyIntegrityBlocked = IsLikelyIntegrityBlocked(targetContext.Handle);
            if (!EnsureTargetForeground(targetContext.Handle))
            {
                var reason = likelyIntegrityBlocked
                    ? InsertionFailureReason.InputBlockedByIntegrity
                    : InsertionFailureReason.FocusRestoreFailed;
                var code = likelyIntegrityBlocked
                    ? "uipi_blocked"
                    : "focus_restore_failed";

                return BuildClipboardOnlyFailure(
                    text,
                    code,
                    reason,
                    $"Could not restore focus to target window ({targetContext}).");
            }

            if (!TryClipboardPaste(text, out string clipboardPasteError))
            {
                Logger.Log($"Clipboard paste failed ({clipboardPasteError}); trying SendInput fallback.");
                if (GetForegroundWindow() == targetContext.Handle
                    && TrySendUnicodeString(text, out string sendInputError))
                {
                    return new InsertResult
                    {
                        Success = true,
                        Method = "SendInputFallback",
                        FailureReason = InsertionFailureReason.None,
                        TargetLocked = true
                    };
                }

                return BuildClipboardOnlyFailure(
                    text,
                    string.IsNullOrWhiteSpace(clipboardPasteError) ? "insert_failed" : clipboardPasteError,
                    InsertionFailureReason.ClipboardUnavailable,
                    "Clipboard paste failed and SendInput fallback was unavailable.");
            }

            return new InsertResult
            {
                Success = true,
                Method = "ClipboardPaste",
                FailureReason = InsertionFailureReason.None,
                TargetLocked = true,
                ClipboardUpdated = true
            };
        }

        private static InsertResult BuildClipboardOnlyFailure(
            string text,
            string errorCode,
            InsertionFailureReason reason,
            string logMessage)
        {
            Logger.Log(logMessage);
            bool copied = TrySetClipboardText(text, out string clipboardError);
            if (!copied)
            {
                Logger.Log($"Failed to copy text to clipboard during recovery ({clipboardError}).");
            }

            return new InsertResult
            {
                Success = false,
                Method = copied ? "ClipboardOnly" : "InsertFailed",
                ErrorCode = copied ? errorCode : $"{errorCode}+{clipboardError}",
                FailureReason = copied ? reason : InsertionFailureReason.ClipboardUnavailable,
                ClipboardUpdated = copied,
                TargetLocked = true
            };
        }

        private static bool EnsureTargetForeground(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero || !IsWindow(hWnd)) return false;
            if (GetForegroundWindow() == hWnd) return true;

            for (int i = 0; i < 8; i++)
            {
                RestoreFocus(hWnd);
                Thread.Sleep(70);
                if (GetForegroundWindow() == hWnd)
                {
                    return true;
                }
            }

            return false;
        }

        private static void RestoreFocus(IntPtr hWnd)
        {
            try
            {
                IntPtr foregroundWindow = GetForegroundWindow();
                uint ignored;
                uint foregroundThreadId = GetWindowThreadProcessId(foregroundWindow, out ignored);
                uint currentThreadId = GetCurrentThreadId();

                AttachThreadInput(currentThreadId, foregroundThreadId, true);
                SetForegroundWindow(hWnd);
                AttachThreadInput(currentThreadId, foregroundThreadId, false);
            }
            catch (Exception ex)
            {
                Logger.LogException("RestoreFocus", ex);
            }
        }

        private static bool TrySendUnicodeString(string text, out string errorCode)
        {
            errorCode = string.Empty;

            try
            {
                foreach (char c in text)
                {
                    var inputs = new INPUT[2];
                    inputs[0] = CreateUnicodeInput(c, true);
                    inputs[1] = CreateUnicodeInput(c, false);

                    uint result = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
                    if (result == 0)
                    {
                        errorCode = $"send_input_{Marshal.GetLastWin32Error()}";
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                errorCode = ex.GetType().Name;
                Logger.LogException("TrySendUnicodeString", ex);
                return false;
            }
        }

        private static INPUT CreateUnicodeInput(char c, bool keyDown)
        {
            return new INPUT
            {
                type = (uint)InputType.Keyboard,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = (ushort)c,
                        dwFlags = (uint)(KeyEventFlags.Unicode | (keyDown ? 0 : KeyEventFlags.KeyUp)),
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
        }

        private static bool TryClipboardPaste(string text, out string errorCode)
        {
            errorCode = string.Empty;
            bool restoreClipboard = Config.ConfigManager.Config.RestoreClipboard;
            bool hadText = false;
            string? originalText = null;

            try
            {
                if (!TrySetClipboardTextWithSnapshot(text, restoreClipboard, out hadText, out originalText, out errorCode))
                {
                    return false;
                }

                SendKeyCombination(0x11, 0x56); // Ctrl+V

                if (restoreClipboard)
                {
                    Thread.Sleep(60);
                    if (!TryRestoreClipboard(hadText, originalText, out var restoreError))
                    {
                        Logger.Log($"Clipboard restore failed: {restoreError}");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                errorCode = ex.GetType().Name;
                Logger.LogException("TryClipboardPaste", ex);
                return false;
            }
        }

        private static bool TrySetClipboardTextWithSnapshot(
            string text,
            bool snapshotOriginal,
            out bool hadText,
            out string? originalText,
            out string errorCode)
        {
            hadText = false;
            originalText = null;
            errorCode = string.Empty;

            for (int attempt = 1; attempt <= 6; attempt++)
            {
                try
                {
                    bool localHadText = false;
                    string? localOriginalText = null;

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (snapshotOriginal && Clipboard.ContainsText())
                        {
                            localHadText = true;
                            localOriginalText = Clipboard.GetText();
                        }

                        Clipboard.SetText(text);
                    });

                    hadText = localHadText;
                    originalText = localOriginalText;
                    return true;
                }
                catch (Exception ex)
                {
                    errorCode = ex.GetType().Name;
                    Thread.Sleep(40);
                }
            }

            return false;
        }

        private static bool TrySetClipboardText(string text, out string errorCode)
        {
            errorCode = string.Empty;

            for (int attempt = 1; attempt <= 6; attempt++)
            {
                try
                {
                    Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(text));
                    return true;
                }
                catch (Exception ex)
                {
                    errorCode = ex.GetType().Name;
                    Thread.Sleep(40);
                }
            }

            if (string.IsNullOrWhiteSpace(errorCode))
            {
                errorCode = "clipboard_unavailable";
            }

            return false;
        }

        private static bool TryRestoreClipboard(bool hadText, string? originalText, out string errorCode)
        {
            errorCode = string.Empty;

            for (int attempt = 1; attempt <= 4; attempt++)
            {
                try
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (!hadText)
                        {
                            Clipboard.Clear();
                            return;
                        }

                        if (originalText != null)
                        {
                            Clipboard.SetText(originalText);
                        }
                        else
                        {
                            Clipboard.Clear();
                        }
                    });

                    return true;
                }
                catch (Exception ex)
                {
                    errorCode = ex.GetType().Name;
                    Thread.Sleep(30);
                }
            }

            return false;
        }

        private static bool IsLikelyIntegrityBlocked(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return false;

            try
            {
                uint processId;
                GetWindowThreadProcessId(hWnd, out processId);
                if (processId == 0) return false;

                IntPtr hProcess = OpenProcess(ProcessQueryLimitedInformation, false, processId);
                if (hProcess == IntPtr.Zero)
                {
                    return Marshal.GetLastWin32Error() == 5;
                }

                CloseHandle(hProcess);
            }
            catch
            {
                // Ignore detection failures.
            }

            return false;
        }

        private static TargetWindowContext BuildContextFromHandle(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
            {
                return TargetWindowContext.Empty;
            }

            uint processId;
            GetWindowThreadProcessId(handle, out processId);

            string processName = string.Empty;
            if (processId != 0)
            {
                try
                {
                    processName = Process.GetProcessById((int)processId).ProcessName;
                }
                catch
                {
                    processName = string.Empty;
                }
            }

            return new TargetWindowContext(
                handle,
                processId,
                processName,
                GetWindowTitle(handle),
                DateTime.UtcNow);
        }

        private static string GetWindowTitle(IntPtr hWnd)
        {
            try
            {
                int length = GetWindowTextLength(hWnd);
                if (length <= 0)
                {
                    return string.Empty;
                }

                var builder = new StringBuilder(length + 1);
                GetWindowText(hWnd, builder, builder.Capacity);
                return builder.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void SendKeyCombination(ushort modifier, ushort key)
        {
            var inputs = new INPUT[]
            {
                new INPUT { type = 1, U = new InputUnion { ki = new KEYBDINPUT { wVk = modifier, dwFlags = 0 } } },
                new INPUT { type = 1, U = new InputUnion { ki = new KEYBDINPUT { wVk = key, dwFlags = 0 } } },
                new INPUT { type = 1, U = new InputUnion { ki = new KEYBDINPUT { wVk = key, dwFlags = 0x0002 } } },
                new INPUT { type = 1, U = new InputUnion { ki = new KEYBDINPUT { wVk = modifier, dwFlags = 0x0002 } } }
            };

            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }
    }
}
