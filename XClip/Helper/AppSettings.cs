using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using SharpHook.Data;

namespace XClip.Helper;

public class AppSettings
{
    public EventMask Modifiers { get; set; } = EventMask.LeftAlt | EventMask.LeftShift;
    public KeyCode Key { get; set; } = KeyCode.VcK;
    public bool IsAutoStartEnabled { get; set; }
}

public static class SettingsManager
{
    private static readonly string FolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "XClip");

    private static readonly string FilePath = Path.Combine(FolderPath, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch
        {
            // Fall back to defaults on read errors
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(FolderPath);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.ReadAllText(FilePath);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }
}