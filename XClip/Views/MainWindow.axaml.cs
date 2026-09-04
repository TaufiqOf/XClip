using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using XClip.Services;
using XClip.ViewModels;

// Required for RoutingStrategies

namespace XClip.Views;

public partial class MainWindow : Window
{
    private const int NumericSelectionMaxDigits = 2;
    private static readonly TimeSpan NumericSelectionDelay = TimeSpan.FromMilliseconds(350);
    private readonly GlobalHotkeyService _hotkeyService;

    private string _inputBuffer = string.Empty;
    private DispatcherTimer? _inputTimer;
    private bool _isClosingForReal;
    private readonly MainViewModel _viewModel;

    public MainWindow(GlobalHotkeyService hotkeyService)
    {
        _viewModel = new MainViewModel(hotkeyService);
        _hotkeyService = hotkeyService;
        DataContext = _viewModel;
        InitializeComponent();
        Opened += OnOpened;
        Closed += OnClosed;

        // Use Tunnel routing strategy to catch key presses before ListBox consumes them
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        _viewModel.OnWindowKeyDown(e);
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
        var windowWidthPixels = (int)(Width * screen.Scaling);
        var windowHeightPixels = (int)(Height * screen.Scaling);

        var x = workArea.X + workArea.Width - windowWidthPixels;
        var y = workArea.Y + workArea.Height - windowHeightPixels;

        Position = new PixelPoint(x, y);
    }

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
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        PositionInBottomRight();

        Activate();
        Dispatcher.UIThread.Post(FocusControls, DispatcherPriority.Input);
    }

    private void FocusControls()
    {
        Activate();

        if (ListBox.SelectedItem == null) ListBox.SelectedIndex = 0;

        var container = ListBox.ContainerFromIndex(ListBox.SelectedIndex);
        if (container != null)
            container.Focus();
        else
            ListBox.Focus();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _inputTimer?.Stop();
        _inputTimer = null;

        if (DataContext is IDisposable disposableVm) disposableVm.Dispose();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        var appIsShuttingDown = (Application.Current as App)?.IsShuttingDown == true;

        if (!_isClosingForReal && !appIsShuttingDown)
        {
            e.Cancel = true;
            HideToTray();
        }

        base.OnClosing(e);
    }
}