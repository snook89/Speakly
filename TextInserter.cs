using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace Speakly.Services
{
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

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

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

        public static void InsertText(string text, IntPtr targetWindow = default)
        {
            if (string.IsNullOrEmpty(text)) 
            {
                Logger.Log("InsertText called with empty string. Skipping.");
                return;
            }

            if (targetWindow != IntPtr.Zero)
            {
                IntPtr current = GetForegroundWindow();
                if (current != targetWindow)
                {
                    Logger.Log($"Restoring focus to window {targetWindow}");
                    RestoreFocus(targetWindow);
                    Thread.Sleep(100); // Give it a moment to settle
                }
            }

            Logger.Log($"Attempting to insert text of length {text.Length}");
            
            try
            {
                CheckForElevation(targetWindow);
                SendUnicodeStringWithDelay(text);
                Logger.Log("SendUnicodeStringWithDelay completed.");
            }
            catch (Exception ex)
            {
                Logger.LogException("InsertText (SendUnicodeString)", ex);
                Logger.Log("Falling back to clipboard insertion.");
                ClipboardInsert(text);
            }
        }

        private static void RestoreFocus(IntPtr hWnd)
        {
            try
            {
                IntPtr foregroundWindow = GetForegroundWindow();
                uint dummy;
                uint foregroundThreadId = GetWindowThreadProcessId(foregroundWindow, out dummy);
                uint targetThreadId = GetWindowThreadProcessId(hWnd, out dummy);
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

        private static void SendUnicodeStringWithDelay(string text)
        {
            foreach (char c in text)
            {
                var inputs = new INPUT[2];
                inputs[0] = CreateUnicodeInput(c, true);
                inputs[1] = CreateUnicodeInput(c, false);

                uint result = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
                if (result == 0)
                {
                    Logger.Log($"SendInput failed for char '{c}'. Error: {Marshal.GetLastWin32Error()}");
                }
                
                // Small delay between characters to bypass Win11 throttling/autocorrect interference
                Thread.Sleep(2); 
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

        private static void CheckForElevation(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;
            try
            {
                uint processId;
                GetWindowThreadProcessId(hWnd, out processId);
                IntPtr hProcess = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, processId);
                if (hProcess == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error == 5) // Access Denied
                    {
                        Logger.Log("WARNING: Target window is likely elevated (Admin). Input may be blocked by UIPI.");
                    }
                }
                else
                {
                    CloseHandle(hProcess);
                }
            }
            catch { }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        private static void SendUnicodeString(string text)
        {
            // Keeping for reference if needed, but switching to WithDelay
            var inputs = new INPUT[text.Length * 2];
            for (int i = 0; i < text.Length; i++)
            {
                inputs[i * 2] = CreateUnicodeInput(text[i], true);
                inputs[i * 2 + 1] = CreateUnicodeInput(text[i], false);
            }
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        private static void ClipboardInsert(string text)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                string original = Clipboard.GetText();
                Clipboard.SetText(text);
                
                // Emulate Ctrl+V
                SendKeyCombination(0x11, 0x56); // VK_CONTROL, 'V'
                
                // Note: Restoring clipboard depends on Config.RestoreClipboard
                // For simplicity, we just set it here.
            });
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
