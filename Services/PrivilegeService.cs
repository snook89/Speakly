using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;

namespace Speakly.Services
{
    public static class PrivilegeService
    {
        public static bool IsCurrentProcessElevated()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public static bool TryRestartElevated()
        {
            try
            {
                var exePath = ResolveCurrentExecutablePath();
                if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                {
                    Logger.Log("Unable to elevate: executable path could not be resolved.");
                    return false;
                }

                var args = Environment.GetCommandLineArgs()
                    .Skip(1)
                    .Select(QuoteArgument);

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = string.Join(" ", args),
                    Verb = "runas",
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory
                };

                Process.Start(psi);
                return true;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // User cancelled the UAC prompt.
                Logger.Log("Elevation request cancelled by user.");
                return false;
            }
            catch (Exception ex)
            {
                Logger.LogException("PrivilegeService.TryRestartElevated", ex);
                return false;
            }
        }

        private static string ResolveCurrentExecutablePath()
        {
            try
            {
                var processPath = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(processPath))
                {
                    return Path.GetFullPath(processPath);
                }
            }
            catch
            {
                // Fallback below.
            }

            return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Speakly.exe"));
        }

        private static string QuoteArgument(string arg)
        {
            if (string.IsNullOrEmpty(arg))
            {
                return "\"\"";
            }

            if (!arg.Contains('\"') && !arg.Contains(' ') && !arg.Contains('\t'))
            {
                return arg;
            }

            return $"\"{arg.Replace("\"", "\\\"")}\"";
        }
    }
}
