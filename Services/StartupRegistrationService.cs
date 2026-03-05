using System;
using System.Diagnostics;
using System.IO;

namespace Speakly.Services
{
    public static class StartupRegistrationService
    {
        private const string StartupTaskName = "Speakly Startup";
        public const string StartupLaunchArgument = "--windows-startup";

        public static bool Reconcile(bool enabled, out string message)
        {
            return enabled
                ? EnsureEnabled(out message)
                : EnsureDisabled(out message);
        }

        public static bool IsEnabled(out string message)
        {
            var query = RunSchtasks($"/Query /TN \"{StartupTaskName}\" /FO LIST");
            if (query.ExitCode == 0)
            {
                message = "Startup task is registered.";
                return true;
            }

            if (IsTaskMissing(query.CombinedOutput))
            {
                message = "Startup task is not registered.";
                return false;
            }

            message = $"Unable to query startup task: {query.Summary}";
            return false;
        }

        private static bool EnsureEnabled(out string message)
        {
            var exePath = ResolveCurrentExecutablePath();
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                message = "Unable to resolve Speakly executable path for startup registration.";
                return false;
            }

            // /TR requires the executable path to be wrapped in quotes as part of the value.
            // Append a dedicated argument so the app can distinguish Windows autostart from
            // a normal manual launch and start minimized only for the former.
            var taskAction = $"\\\"{exePath}\\\" {StartupLaunchArgument}";
            var create = RunSchtasks(
                $"/Create /F /TN \"{StartupTaskName}\" /SC ONLOGON /RL LIMITED /TR \"{taskAction}\"");

            if (create.ExitCode == 0)
            {
                message = "Startup task registered.";
                return true;
            }

            message = $"Failed to register startup task: {create.Summary}";
            return false;
        }

        private static bool EnsureDisabled(out string message)
        {
            var delete = RunSchtasks($"/Delete /F /TN \"{StartupTaskName}\"");
            if (delete.ExitCode == 0 || IsTaskMissing(delete.CombinedOutput))
            {
                message = "Startup task disabled.";
                return true;
            }

            message = $"Failed to disable startup task: {delete.Summary}";
            return false;
        }

        private static string ResolveCurrentExecutablePath()
        {
            try
            {
                var fromProcess = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(fromProcess))
                {
                    return Path.GetFullPath(fromProcess);
                }
            }
            catch
            {
                // Fallback below.
            }

            var fallback = Path.Combine(AppContext.BaseDirectory, "Speakly.exe");
            return Path.GetFullPath(fallback);
        }

        private static bool IsTaskMissing(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var normalized = text.ToLowerInvariant();
            return normalized.Contains("cannot find", StringComparison.Ordinal)
                || normalized.Contains("cannot find the file", StringComparison.Ordinal)
                || normalized.Contains("cannot find the path", StringComparison.Ordinal)
                || normalized.Contains("cannot find the task", StringComparison.Ordinal)
                || normalized.Contains("cannot find the specified", StringComparison.Ordinal);
        }

        private static SchtasksResult RunSchtasks(string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", arguments)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    return new SchtasksResult(-1, string.Empty, "Failed to start schtasks.exe.");
                }

                string stdOut = process.StandardOutput.ReadToEnd();
                string stdErr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                return new SchtasksResult(process.ExitCode, stdOut, stdErr);
            }
            catch (Exception ex)
            {
                return new SchtasksResult(-1, string.Empty, ex.Message);
            }
        }

        private readonly struct SchtasksResult
        {
            public SchtasksResult(int exitCode, string stdOut, string stdErr)
            {
                ExitCode = exitCode;
                StdOut = stdOut ?? string.Empty;
                StdErr = stdErr ?? string.Empty;
            }

            public int ExitCode { get; }
            public string StdOut { get; }
            public string StdErr { get; }

            public string CombinedOutput =>
                string.IsNullOrWhiteSpace(StdErr)
                    ? StdOut
                    : $"{StdOut}{Environment.NewLine}{StdErr}".Trim();

            public string Summary
            {
                get
                {
                    var raw = CombinedOutput;
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        return $"exit code {ExitCode}";
                    }

                    var trimmed = raw.Trim();
                    if (trimmed.Length <= 320)
                    {
                        return $"{trimmed} (exit code {ExitCode})";
                    }

                    return $"{trimmed[..320]}... (exit code {ExitCode})";
                }
            }
        }
    }
}
