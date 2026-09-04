using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Threading;
using SharpHook;
using SharpHook.Data;
using SharpHook.Providers;
using SharpHook.Simulation;

namespace XClip.Services;

public class GlobalHotkeyService : IDisposable
{
    private readonly EventLoopGlobalHook? _hook;
    private readonly bool _isWaylandSession;
    private readonly Action _onHotKeyPressed;
    private readonly EventSimulator? _simulator;

    public GlobalHotkeyService(Action onHotKeyPressed)
    {
        _onHotKeyPressed = onHotKeyPressed;
        _isWaylandSession = IsWaylandSession();

        if (!_isWaylandSession)
        {
            _simulator = EventSimulator.Create("XClip", UioHookProvider.Instance);
            _hook = new EventLoopGlobalHook(UioHookProvider.Instance);
            _hook.KeyPressed += OnKeyPressed;
        }
    }

    public EventMask TargetModifiers { get; set; } = EventMask.LeftAlt | EventMask.LeftShift;
    public KeyCode TargetKey { get; set; } = KeyCode.VcK;

    // Always true now, as we provide Wayland-compatible fallback mechanisms
    public bool IsSupported => true;

    public void Dispose()
    {
        if (_hook != null)
        {
            _hook.KeyPressed -= OnKeyPressed;
            _hook.Dispose();
        }
    }

    public void Start()
    {
        if (!_isWaylandSession) _hook?.RunAsync();
    }

    public async Task SimulatePasteAsync()
    {
        await Task.Delay(100);

        if (_isWaylandSession)
        {
            await SimulateWaylandPasteAsync();
            return;
        }

        if (_simulator == null) return;

        var isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        var modifierKey = isMac ? KeyCode.VcLeftMeta : KeyCode.VcLeftControl;

        // X11 / Windows / macOS Simulation
        _simulator.SimulateKeyPress(modifierKey);
        _simulator.SimulateKeyPress(KeyCode.VcV);

        _simulator.SimulateKeyRelease(KeyCode.VcV);
        _simulator.SimulateKeyRelease(modifierKey);
    }

    private static async Task SimulateWaylandPasteAsync()
    {
        // 1. Try 'wtype' first (Wayland native virtual keyboard)
        if (IsToolInstalled("wtype"))
        {
            RunProcess("wtype", "-M ctrl -k v -m ctrl");
            return;
        }

        // 2. Try 'ydotool' as fallback (Works via uinput daemon across all compositors)
        if (IsToolInstalled("ydotool"))
        {
            RunProcess("ydotool", "key 29:1 47:1 47:0 29:0"); // 29 = Ctrl, 47 = V
            return;
        }

        await Task.CompletedTask;
    }

    // Call this method directly if triggered via a CLI flag or D-Bus signal from system shortcut
    public void TriggerHotkeyFromSystem()
    {
        Dispatcher.UIThread.Post(_onHotKeyPressed);
    }

    private static bool IsWaylandSession()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return false;

        var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        if (string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase)) return true;

        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        var currentMask = e.RawEvent.Mask;

        var modifiersMatch = (currentMask & TargetModifiers) == TargetModifiers;
        var keyMatches = e.Data.KeyCode == TargetKey;

        if (modifiersMatch && keyMatches) Dispatcher.UIThread.Post(_onHotKeyPressed);
    }

    public void UpdateHotkey(EventMask modifiers, KeyCode key)
    {
        TargetModifiers = modifiers;
        TargetKey = key;
    }

    private static bool IsToolInstalled(string toolName)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "which",
                Arguments = toolName,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            process?.WaitForExit();
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void RunProcess(string fileName, string args)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true
            })?.WaitForExit();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[XClip] Failed to run {fileName}: {ex.Message}");
        }
    }
}