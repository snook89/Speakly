using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;

namespace Speakly.Services
{
    public class HotkeyEventArgs : EventArgs
    {
        public Key Key { get; }
        public Key SystemKey { get; }
        public HotkeyEventArgs(Key key, Key systemKey)
        {
            Key = key;
            SystemKey = systemKey;
        }
    }

    public class GlobalHotkeyService : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        private LowLevelKeyboardProc? _proc;
        private IntPtr _hookID = IntPtr.Zero;
        private readonly object _pressedKeysLock = new();
        private readonly HashSet<Key> _pressedKeys = new();

        public bool IsHookInstalled => _hookID != IntPtr.Zero;
        public int HookInitializationErrorCode { get; private set; }
        public string HookInitializationError { get; private set; } = string.Empty;

        public event EventHandler<HotkeyEventArgs>? KeyDown;
        public event EventHandler<HotkeyEventArgs>? KeyUp;

        public GlobalHotkeyService()
        {
            try
            {
                _proc = HookCallback;
                _hookID = SetHook(_proc);
                if (_hookID == IntPtr.Zero)
                {
                    HookInitializationErrorCode = Marshal.GetLastWin32Error();
                    HookInitializationError = HookInitializationErrorCode == 0
                        ? "keyboard_hook_install_failed"
                        : $"keyboard_hook_install_failed:{HookInitializationErrorCode}";
                }
            }
            catch (Exception ex)
            {
                HookInitializationError = ex.GetType().Name;
                HookInitializationErrorCode = Marshal.GetLastWin32Error();
            }
        }

        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule? curModule = curProcess.MainModule)
            {
                if (curModule == null)
                {
                    throw new InvalidOperationException("Could not get current process's main module.");
                }
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc,
                    GetModuleHandle(curModule.ModuleName!), 0);
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                Key key = KeyInterop.KeyFromVirtualKey(vkCode);

                if (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
                {
                    lock (_pressedKeysLock)
                    {
                        _pressedKeys.Add(key);
                    }

                    KeyDown?.Invoke(this, new HotkeyEventArgs(key, wParam == (IntPtr)WM_SYSKEYDOWN ? key : Key.None));
                }
                else if (wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP)
                {
                    lock (_pressedKeysLock)
                    {
                        _pressedKeys.Remove(key);
                    }

                    KeyUp?.Invoke(this, new HotkeyEventArgs(key, wParam == (IntPtr)WM_SYSKEYUP ? key : Key.None));
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        public bool IsKeyPressed(Key key)
        {
            lock (_pressedKeysLock)
            {
                return _pressedKeys.Contains(key);
            }
        }

        public void Dispose()
        {
            lock (_pressedKeysLock)
            {
                _pressedKeys.Clear();
            }

            if (_hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookID);
            }

            GC.SuppressFinalize(this);
        }

        // --- P/Invoke Definitions ---

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}
