using System;
using Avalonia.Controls;
using ClipboardManagerX.ViewModels;

namespace ClipboardManagerX.Views;

public partial class MainWindow : Window
{
    private bool _isClosingForReal;

    public MainWindow()
    {
        DataContext = new MainViewModel();
        InitializeComponent();
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        PositionInBottomRight();
    }

    private void PositionInBottomRight()
    {
        var screen = Screens.Primary;
        if (screen == null) return;

        var workArea = screen.WorkingArea;
        int windowWidthPixels = (int)(Width * screen.Scaling);
        int windowHeightPixels = (int)(Height * screen.Scaling);

        int x = workArea.X + workArea.Width - windowWidthPixels;
        int y = workArea.Y + workArea.Height - windowHeightPixels;

        Position = new Avalonia.PixelPoint(x, y);
    }

    // Public method called by App.axaml.cs during tray exit
    public void ForceExit()
    {
        _isClosingForReal = true;
        Close();
    }

    public void HideToTray()
    {
        ShowInTaskbar = false;
        Hide();
    }

    public void ShowFromTray()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;
        Activate();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Intercept close button ('X') and send to tray instead of exiting
        if (!_isClosingForReal)
        {
            e.Cancel = true;
            HideToTray();
        }
        base.OnClosing(e);
    }
}