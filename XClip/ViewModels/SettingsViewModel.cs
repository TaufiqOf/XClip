using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SharpHook.Data;
using XClip.Services;

namespace XClip.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly GlobalHotkeyService _hotkeyService;

    [ObservableProperty] private string _hotkeyDisplay;

    public SettingsViewModel(GlobalHotkeyService hotkeyService)
    {
        _hotkeyService = hotkeyService;

        // Initialize with current settings
        PendingModifiers = _hotkeyService.TargetModifiers;
        PendingKey = _hotkeyService.TargetKey;
        HotkeyDisplay = $"{PendingModifiers} + {PendingKey}".Replace("Left", "").Replace("Right", "");
    }

    public EventMask PendingModifiers { get; private set; }
    public KeyCode PendingKey { get; private set; }

    public void SetHotkey(EventMask modifiers, KeyCode key, string display)
    {
        PendingModifiers = modifiers;
        PendingKey = key;
        HotkeyDisplay = display;
    }

    [RelayCommand]
    private void ClearHotkey()
    {
        PendingModifiers = EventMask.LeftAlt | EventMask.LeftShift;
        PendingKey = KeyCode.VcK;
        HotkeyDisplay = "Alt + Shift + K";
    }

    [RelayCommand]
    private void Save()
    {
        // 1. Update active runtime hotkey configuration
        _hotkeyService.UpdateHotkey(PendingModifiers, PendingKey);

        // 2. Persist to disk
        var settings = new AppSettings
        {
            Modifiers = PendingModifiers,
            Key = PendingKey,
            IsAutoStartEnabled = AutoStartManager.IsEnabled()
        };

        SettingsManager.Save(settings);
    }
}