using System;
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
    private readonly EventLoopGlobalHook _hook;
    private readonly EventSimulator _simulator;
    private readonly Action _onHotKeyPressed;
    
    public EventMask TargetModifiers { get; set; } = EventMask.LeftAlt | EventMask.LeftShift;
    public KeyCode TargetKey { get; set; } = KeyCode.VcK;

    public GlobalHotkeyService(Action onHotKeyPressed)
    {
        _onHotKeyPressed = onHotKeyPressed;
        _simulator = EventSimulator.Create("XClip", UioHookProvider.Instance);
        _hook = new EventLoopGlobalHook(UioHookProvider.Instance);
        _hook.KeyPressed += OnKeyPressed;
    }

    public void Start() => _hook.RunAsync();

    public async Task SimulatePasteAsync()
    {
        await Task.Delay(100);

        bool isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        var modifierKey = isMac ? KeyCode.VcLeftMeta : KeyCode.VcLeftControl;

        // Simulate Key Presses
        _simulator.SimulateKeyPress(modifierKey);
        _simulator.SimulateKeyPress(KeyCode.VcV);

        // Simulate Key Releases
        _simulator.SimulateKeyRelease(KeyCode.VcV);
        _simulator.SimulateKeyRelease(modifierKey);
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        var currentMask = e.RawEvent.Mask;
        
        bool modifiersMatch = (currentMask & TargetModifiers) == TargetModifiers;
        bool keyMatches = e.Data.KeyCode == TargetKey;

        if (modifiersMatch && keyMatches)
        {
            Dispatcher.UIThread.Post(_onHotKeyPressed);
        }
    }

    public void UpdateHotkey(EventMask modifiers, KeyCode key)
    {
        TargetModifiers = modifiers;
        TargetKey = key;
    }

    public void Dispose()
    {
        _hook.KeyPressed -= OnKeyPressed;
        _hook.Dispose();
    }
}