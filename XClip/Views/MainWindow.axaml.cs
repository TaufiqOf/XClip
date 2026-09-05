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
        _viewModel.OnHideToTray += HideToTray;
        _hotkeyService = hotkeyService;
        DataContext = _viewModel;
        InitializeComponent();
        Opened += OnOpened;
        Closed += OnClosed;
        Deactivated += OnWindowDeactivated;
        DoubleTapped += OnDoubleTapped;
        // Use Tunnel routing strategy to catch key presses before ListBox consumes them
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        HideToTray();
        _ = _viewModel.DoubleClickAsync();
        _ = _hotkeyService.SimulatePasteAsync();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        if (IsVisible)
        {
            HideToTray();
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            SearchTextBox.Focus();
            SearchTextBox.SelectAll();
        }
        if(e.Key == Key.Escape)
        {
            e.Handled = true;
            HideToTray();
        }

        if (e.Key== Key.Enter)
        {
            HideToTray();
            _ = _viewModel.DoubleClickAsync();
            _ = _viewModel.SimulatePasteAsync();
        }

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
        // Bring window to front natively
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();

        // Give the OS window manager a frame to settle activation before setting control focus
        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is MainViewModel vm && vm.FilteredHistory.Any())
            {
                if (ListBox.SelectedIndex < 0)
                    ListBox.SelectedIndex = 0;

                var container = ListBox.ContainerFromIndex(ListBox.SelectedIndex);
                if (container is Control control)
                {
                    control.Focus();
                }
                else
                {
                    ListBox.Focus();
                }
            }
            else
            {
                ListBox.Focus();
            }
        }, DispatcherPriority.Render);
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