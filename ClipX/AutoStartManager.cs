using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ClipX;

public static class AutoStartManager
{
    private const string AppName = "ClipX";

    public static bool IsEnabled()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
            return key?.GetValue(AppName) != null;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            string desktopFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "autostart", $"{AppName}.desktop");
            return File.Exists(desktopFile);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string plistFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "LaunchAgents", $"com.clipboardmanagerx.autostart.plist");
            return File.Exists(plistFile);
        }

        return false;
    }

    public static void SetEnabled(bool enable)
    {
        string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exePath)) return;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (enable)
                key?.SetValue(AppName, $"\"{exePath}\"");
            else
                key?.DeleteValue(AppName, false);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            string autostartDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "autostart");
            string desktopFile = Path.Combine(autostartDir, $"{AppName}.desktop");

            if (enable)
            {
                Directory.CreateDirectory(autostartDir);
                string content = $"[Desktop Entry]\nType=Application\nName={AppName}\nExec=\"{exePath}\" --autostart\nTerminal=false\nX-GNOME-Autostart-enabled=true\n";
                File.WriteAllText(desktopFile, content);
            }
            else if (File.Exists(desktopFile))
            {
                File.Delete(desktopFile);
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string launchAgentsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "LaunchAgents");
            string plistFile = Path.Combine(launchAgentsDir, "com.clipboardmanagerx.autostart.plist");

            if (enable)
            {
                Directory.CreateDirectory(launchAgentsDir);
                string plistContent = $"""
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
}