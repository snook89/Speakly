using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Speakly.Config;

namespace Speakly.Services
{
    public static class ProfileResolverService
    {
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        public static AppProfile ResolveForForegroundWindow(IntPtr hWnd)
        {
            var config = ConfigManager.Config;
            if (config.Profiles.Count == 0)
            {
                config.Profiles.Add(ConfigManager.BuildDefaultProfile(config));
            }

            if (hWnd != IntPtr.Zero && TryGetProcessName(hWnd, out var processName))
            {
                foreach (var profile in config.Profiles)
                {
                    if (ProfileHelpers.MatchesProcess(profile, processName))
                        return profile;
                }
            }

            var active = config.Profiles.Find(p =>
                string.Equals(p.Id, config.ActiveProfileId, StringComparison.OrdinalIgnoreCase));

            return active ?? config.Profiles[0];
        }

        private static bool TryGetProcessName(IntPtr hWnd, out string processName)
        {
            processName = string.Empty;
            try
            {
                _ = GetWindowThreadProcessId(hWnd, out uint processId);
                if (processId == 0) return false;

                using var proc = Process.GetProcessById((int)processId);
                processName = ProfileHelpers.NormalizeProcessName(proc.ProcessName);
                return !string.IsNullOrWhiteSpace(processName);
            }
            catch (Win32Exception) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (ArgumentException) { return false; }
        }
    }
}
