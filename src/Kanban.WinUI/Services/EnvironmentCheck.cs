using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Windows.ApplicationModel.DynamicDependency;

namespace Kanban_WinUI.Services
{
    /// <summary>
    /// Background prerequisite scan for the framework-dependent build. Runs in Program.Main
    /// before any WinUI type is touched, so a machine that is missing a component gets a
    /// plain, readable list instead of a cryptic COM crash. No UI of its own: the caller
    /// reports failures through the OS message box and the startup log.
    ///
    /// Note on .NET: if the .NET Desktop Runtime is absent entirely, Windows shows its own
    /// "install .NET" dialog and no managed code (including this) ever runs. The check still
    /// validates the version it is running on, which catches a too-old runtime.
    /// </summary>
    public static class EnvironmentCheck
    {
        private static readonly Version MinimumWindows = new(10, 0, 17763);   // matches TargetPlatformMinVersion
        private const int MinimumDotNetMajor = 10;                            // matches TargetFramework net10.0

        /// <summary>
        /// Verifies every prerequisite and, on success, initializes the Windows App Runtime.
        /// Returns true when the app may start; otherwise <paramref name="report"/> lists what is missing.
        /// </summary>
        public static bool TryValidate(out string report)
        {
            var missing = new List<string>();

            // 1. .NET runtime
            if (Environment.Version.Major < MinimumDotNetMajor)
            {
                missing.Add($".NET {MinimumDotNetMajor} Desktop Runtime (x64) — found {RuntimeInformation.FrameworkDescription}. " +
                            "Download: https://dotnet.microsoft.com/download/dotnet/10.0");
            }

            // 2. Windows version
            var os = Environment.OSVersion.Version;
            if (os < MinimumWindows)
            {
                missing.Add($"Windows 10 build {MinimumWindows.Build} or later — found {os}.");
            }

            // 3. Windows App Runtime (also loads it for this process when present)
            if (!TryInitializeWindowsAppRuntime(out string runtimeDetail))
            {
                missing.Add(runtimeDetail);
            }

            report = string.Join(Environment.NewLine, missing.Select(m => "  •  " + m));
            Log(missing.Count == 0
                ? $"Environment pre-check passed ({RuntimeInformation.FrameworkDescription}, Windows {os})"
                : "Environment pre-check FAILED:" + Environment.NewLine + report);
            return missing.Count == 0;
        }

        /// <summary>
        /// Bootstraps the installed Windows App Runtime. This is both the check and the load:
        /// the framework-dependent build has no private copy of the runtime, so if the machine
        /// lacks a compatible version this returns false instead of crashing later in WinUI.
        /// </summary>
        private static bool TryInitializeWindowsAppRuntime(out string detail)
        {
            try
            {
                uint majorMinor = Microsoft.WindowsAppSDK.Release.MajorMinor;
                string versionTag = Microsoft.WindowsAppSDK.Release.VersionTag;
                var minVersion = new PackageVersion(Microsoft.WindowsAppSDK.Runtime.Version.UInt64);

                // OnPackageIdentity_NOOP: harmlessly skip when the app does run with MSIX identity.
                if (Bootstrap.TryInitialize(majorMinor, versionTag, minVersion,
                        Bootstrap.InitializeOptions.OnPackageIdentity_NOOP, out int hr))
                {
                    detail = "";
                    return true;
                }

                detail = $"Windows App Runtime {Format(majorMinor)} (x64) — not installed or older than " +
                         $"{minVersion.Major}.{minVersion.Minor}.{minVersion.Build}.{minVersion.Revision} " +
                         $"(HRESULT 0x{hr:X8}). Download the Windows App Runtime redistributable: " +
                         "https://aka.ms/windowsappsdk/downloads";
                return false;
            }
            catch (Exception ex)
            {
                detail = "Windows App Runtime (x64) — could not be loaded: " + ex.Message +
                         ". Download: https://aka.ms/windowsappsdk/downloads";
                return false;
            }
        }

        private static string Format(uint majorMinor) => $"{(majorMinor >> 16) & 0xFFFF}.{majorMinor & 0xFFFF}";

        /// <summary>Plain file logging: deliberately touches no WinUI types (they may not be loadable yet).</summary>
        internal static void Log(string message)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(Path.GetTempPath(), "Kanban.WinUI.Startup.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
            catch { /* logging must never block startup */ }
        }
    }
}
