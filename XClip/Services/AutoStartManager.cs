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

    public static bool IsEnabled()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            string desktopFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "autostart", $"{FlatpakId ?? AppName}.desktop");
            return File.Exists(desktopFile);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
            return key?.GetValue(AppName) != null;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            string plistFile = Path.Combine(
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
            // Point to real host path if running in Flatpak
            string autostartDir = IsFlatpak 
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "autostart")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "autostart");

            string fileName = IsFlatpak ? $"{FlatpakId}.desktop" : $"{AppName}.desktop";
            string desktopFile = Path.Combine(autostartDir, fileName);

            if (enable)
            {
                Directory.CreateDirectory(autostartDir);
                
                // Use 'flatpak run <AppID>' for Flatpak executions
                string execLine = IsFlatpak 
                    ? $"Exec=flatpak run {FlatpakId} --autostart"
                    : $"Exec=\"{Process.GetCurrentProcess().MainModule?.FileName}\" --autostart";

                string content = $"""
                                  [Desktop Entry]
                                  Type=Application
                                  Name={AppName}
                                  {execLine}
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