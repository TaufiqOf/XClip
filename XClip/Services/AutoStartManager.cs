using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace XClip.Services;

public static class AutoStartManager
{
    private const string AppName = "XClip";
    private static readonly string? FlatpakId = Environment.GetEnvironmentVariable("FLATPAK_ID");
    public static bool IsFlatpak => !string.IsNullOrEmpty(FlatpakId);

    private static string LinuxAutostartDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".config", "autostart");

    private static string LinuxDesktopFilePath
    {
        get
        {
            var fileName = IsFlatpak && !string.IsNullOrWhiteSpace(FlatpakId)
                ? $"{FlatpakId}.desktop"
                : $"{AppName}.desktop";
            return Path.Combine(LinuxAutostartDir, fileName);
        }
    }

    public static bool IsEnabled()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return File.Exists(LinuxDesktopFilePath);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
            return key?.GetValue(AppName) != null;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var plistFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "LaunchAgents", "com.clipboardmanagerx.autostart.plist");
            return File.Exists(plistFile);
        }

        return false;
    }

    public static void SetEnabled(bool enable)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var desktopFile = LinuxDesktopFilePath;

            if (enable)
            {
                if (!TryGetLinuxExecCommand(out var execCommand)) return;

                Directory.CreateDirectory(LinuxAutostartDir);

                var content = $"""
                               [Desktop Entry]
                               Type=Application
                               Name={AppName}
                               Exec={execCommand}
                               Terminal=false
                               X-GNOME-Autostart-enabled=true
                               """;

                File.WriteAllText(desktopFile, content);
            }
            else if (File.Exists(desktopFile))
            {
                File.Delete(desktopFile);
            }

            return;
        }

        // Standard Windows / macOS implementation
        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exePath)) return;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (enable)
                key?.SetValue(AppName, $"\"{exePath}\"");
            else
                key?.DeleteValue(AppName, false);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var launchAgentsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "LaunchAgents");
            var plistFile = Path.Combine(launchAgentsDir, "com.clipboardmanagerx.autostart.plist");

            if (enable)
            {
                Directory.CreateDirectory(launchAgentsDir);
                var plistContent = $"""
                                    <?xml version="1.0" encoding="UTF-8"?>
                                    <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
                                    <plist version="1.0">
                                    <dict>
                                        <key>Label</key>
                                        <string>com.clipboardmanagerx.autostart</string>
                                        <key>ProgramArguments</key>
                                        <array>
                                            <string>{exePath}</string>
                                            <string>--autostart</string>
                                        </array>
                                        <key>RunAtLoad</key>
                                        <true/>
                                    </dict>
                                    </plist>
                                    """;
                File.WriteAllText(plistFile, plistContent);
            }
            else if (File.Exists(plistFile))
            {
                File.Delete(plistFile);
            }
        }
    }

    private static bool TryGetLinuxExecCommand(out string execCommand)
    {
        if (IsFlatpak && !string.IsNullOrWhiteSpace(FlatpakId))
        {
            execCommand = $"flatpak run {FlatpakId} --autostart";
            return true;
        }

        var appImagePath = Environment.GetEnvironmentVariable("APPIMAGE");
        if (!string.IsNullOrWhiteSpace(appImagePath) && File.Exists(appImagePath))
        {
            execCommand = $"\"{EscapeDesktopExecArg(appImagePath)}\" --autostart";
            return true;
        }

        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrWhiteSpace(exePath))
        {
            execCommand = $"\"{EscapeDesktopExecArg(exePath)}\" --autostart";
            return true;
        }

        execCommand = string.Empty;
        return false;
    }

    private static string EscapeDesktopExecArg(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}