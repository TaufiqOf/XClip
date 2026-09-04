using System;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using XClip.Services;
using XClip.Views;

namespace XClip;

public class App : Application
{
    private const string PipeName = "XClip_IPC_Pipe";
    private GlobalHotkeyService? _hotkeyService;
    private bool _isCleanedUp;

    private TrayIcon? _trayIcon;

    public bool IsShuttingDown { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? Array.Empty<string>();
            var isToggleRequested = args.Contains("--toggle-window", StringComparer.OrdinalIgnoreCase);

            // Check if a primary instance is already running
            if (CanConnectToExistingInstance(isToggleRequested))
            {
                // Exit the secondary process immediately before Avalonia starts its MainLoop
                Environment.Exit(0);
                return;
            }

            // --- PRIMARY INSTANCE SETUP ---
            var settings = SettingsManager.Load();

            _hotkeyService = new GlobalHotkeyService(ToggleMainWindow)
            {
                TargetModifiers = settings.Modifiers,
                TargetKey = settings.Key
            };

            if (_hotkeyService.IsSupported) _hotkeyService.Start();

            var mainWindow = new MainWindow(_hotkeyService);
            desktop.MainWindow = mainWindow;

            desktop.ShutdownRequested += OnShutdownRequested;
            desktop.Exit += OnDesktopExit;

            // Start background IPC server listener
            _ = StartIpcListenerAsync();

            if (isToggleRequested) Dispatcher.UIThread.Post(ToggleMainWindow);
        }

        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        var trayIcons = TrayIcon.GetIcons(this);
        if (trayIcons != null && trayIcons.Count > 0)
        {
            _trayIcon = trayIcons[0];
            _trayIcon.ToolTipText = "XClip";
        }

        UpdateIcons(ActualThemeVariant);
        ActualThemeVariantChanged += OnActualThemeVariantChanged;

        base.OnFrameworkInitializationCompleted();
    }

    private static bool CanConnectToExistingInstance(bool isToggleRequested)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(150); // Short timeout check

            if (isToggleRequested)
            {
                using var writer = new StreamWriter(client);
                writer.WriteLine("--toggle-window");
                writer.Flush();
            }

            return true; // Connection succeeded -> Secondary instance
        }
        catch
        {
            return false; // Connection failed -> Primary instance
        }
    }


    private async Task StartIpcListenerAsync()
    {
        while (!IsShuttingDown)
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync();

                using var reader = new StreamReader(server);
                var message = await reader.ReadLineAsync();

                if (message == "--toggle-window") Dispatcher.UIThread.Post(ToggleMainWindow);
            }
            catch
            {
                // Ignore pipe interrupts during application shutdown
            }
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        UpdateIcons(ActualThemeVariant);
    }

    private void UpdateIcons(ThemeVariant theme)
    {
        var assetUri = theme == ThemeVariant.Dark
            ? "avares://XClip/Assets/icon-dark.ico"
            : "avares://XClip/Assets/icon-light.ico";

        var uri = new Uri(assetUri);

        Dispatcher.UIThread.Post(() =>
        {
            if (_trayIcon != null)
            {
                using var trayStream = AssetLoader.Open(uri);
                _trayIcon.Icon = new WindowIcon(trayStream);
            }

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null)
            {
                using var windowStream = AssetLoader.Open(uri);
                desktop.MainWindow.Icon = new WindowIcon(windowStream);
            }
        });
    }

    private void TrayIcon_OnClicked(object? sender, EventArgs e)
    {
        ToggleMainWindow();
    }

    private void ShowApp_OnClick(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow is MainWindow window)
            window.ShowFromTray();
    }

    private void ExitApp_OnClick(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            IsShuttingDown = true;
            if (desktop.MainWindow is MainWindow window) window.ForceExit();

            CleanupResources();
            desktop.Shutdown();
        }
    }

    private void ToggleMainWindow()
    {
        if (IsShuttingDown) return;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow is MainWindow window)
        {
            if (window is { IsVisible: true, IsActive: true })
                window.HideToTray();
            else
                window.ShowFromTray();
        }
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        IsShuttingDown = true;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow is MainWindow window)
            window.ForceExit();

        CleanupResources();
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        IsShuttingDown = true;
        CleanupResources();
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        IsShuttingDown = true;
        CleanupResources();
    }

    private void CleanupResources()
    {
        if (_isCleanedUp) return;

        _isCleanedUp = true;
        ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        _hotkeyService?.Dispose();
        _hotkeyService = null;
    }
}