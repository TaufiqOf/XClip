using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using ClipX.Views;

namespace ClipX;

public partial class App : Application
{
    private TrayIcon? _trayIcon;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        var trayIcons = TrayIcon.GetIcons(this);
        if (trayIcons != null && trayIcons.Count > 0)
        {
            _trayIcon = trayIcons[0];
            _trayIcon.ToolTipText = "ClipX";
        }

        // Apply initial icon matching current theme
        UpdateIcons(ActualThemeVariant);

        ActualThemeVariantChanged += OnActualThemeVariantChanged;
        base.OnFrameworkInitializationCompleted();
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        UpdateIcons(ActualThemeVariant);
    }

    private void UpdateIcons(ThemeVariant theme)
    {
        string assetUri = theme == ThemeVariant.Dark
            ? "avares://ClipX/Assets/icon-dark.ico"
            : "avares://ClipX/Assets/icon-light.ico";

        var uri = new Uri(assetUri);

        Dispatcher.UIThread.Post(() =>
        {
            // Open stream separately for TrayIcon
            if (_trayIcon != null)
            {
                using var trayStream = AssetLoader.Open(uri);
                _trayIcon.Icon = new WindowIcon(trayStream);
            }

            // Open fresh stream for MainWindow
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
        {
            window.ShowFromTray();
        }
    }

    private void ExitApp_OnClick(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.MainWindow is MainWindow window)
            {
                window.ForceExit();
            }

            desktop.Shutdown();
        }
    }

    private void ToggleMainWindow()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow is MainWindow window)
        {
            if (window.IsVisible)
                window.HideToTray();
            else
                window.ShowFromTray();
        }
    }
}