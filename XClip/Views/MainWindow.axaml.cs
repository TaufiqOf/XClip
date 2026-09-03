using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity; // Required for RoutingStrategies
using Avalonia.Threading;
using XClip.Services;
using XClip.ViewModels;

namespace XClip.Views;

public partial class MainWindow : Window
{
    private readonly GlobalHotkeyService _hotkeyService;
    private bool _isClosingForReal;
    private const int NumericSelectionMaxDigits = 2;
    private static readonly TimeSpan NumericSelectionDelay = TimeSpan.FromMilliseconds(350);

    public MainWindow(GlobalHotkeyService hotkeyService)
    {
        _hotkeyService = hotkeyService;
        DataContext = new MainViewModel(_hotkeyService);
        InitializeComponent();
        Opened += OnOpened;
        Closed += OnClosed;

        // Use Tunnel routing strategy to catch key presses before ListBox consumes them
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
    }

    private string _inputBuffer = string.Empty;
    private DispatcherTimer? _inputTimer;

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        // Handle Ctrl+S key combination to focus SearchTextBox
        if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            e.Handled = true;
            SearchTextBox.Focus();
            SearchTextBox.SelectAll();
            return;
        }

        if (DataContext is MainViewModel vm)
        {
            // Don't intercept digit shortcuts if user is currently typing in SearchTextBox
            if (!SearchTextBox.IsFocused && TryGetDigitFromKey(e.Key, out var digit))
            {
                if (await HandleBufferedNumericSelectionAsync(vm, digit))
                {
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                if (vm.SelectedItem != null)
                {
                    e.Handled = true;
                    HideToTray();
                    await vm.CopyAsync(vm.SelectedItem);
                    await _hotkeyService.SimulatePasteAsync();
                }
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                HideToTray();
            }
        }
    }

    private async Task ProcessBufferSelectionAsync(MainViewModel vm)
    {
        if (int.TryParse(_inputBuffer, out int targetNumber) && targetNumber > 0)
        {
            int targetIndex = targetNumber - 1; // Convert 1-based display to 0-based index

            if (targetIndex < vm.FilteredHistory.Count)
            {
                vm.SelectAndPasteByIndex(targetIndex);
                HideToTray();
                await vm.CopyAsync(vm.SelectedItem);
                await _hotkeyService.SimulatePasteAsync();
            }
        }

        _inputBuffer = string.Empty; // Clear buffer
    }

    private async Task<bool> HandleBufferedNumericSelectionAsync(MainViewModel vm, int digit)
    {
        if (string.IsNullOrEmpty(_inputBuffer) && digit == 0)
        {
            return false;
        }

        _inputBuffer += digit;

        _inputTimer ??= new DispatcherTimer();
        _inputTimer.Stop();
        _inputTimer.Interval = NumericSelectionDelay;
        _inputTimer.Tick -= OnInputTimerTick;
        _inputTimer.Tick += OnInputTimerTick;

        if (_inputBuffer.Length >= NumericSelectionMaxDigits)
        {
            await ProcessBufferSelectionAsync(vm);
            _inputTimer.Stop();
        }
        else
        {
            _inputTimer.Start();
        }

        return true;
    }

    private async void OnInputTimerTick(object? sender, EventArgs e)
    {
        _inputTimer?.Stop();

        if (DataContext is MainViewModel vm)
        {
            await ProcessBufferSelectionAsync(vm);
        }
        else
        {
            _inputBuffer = string.Empty;
        }
    }

    private static bool TryGetDigitFromKey(Key key, out int digit)
    {
        digit = -1;

        if (key >= Key.D0 && key <= Key.D9)
        {
            digit = key - Key.D0;
            return true;
        }

        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            digit = key - Key.NumPad0;
            return true;
        }

        return false;
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

        if (DataContext is MainViewModel vm && vm.ClipboardHistory.Any())
        {
            if (ListBox.SelectedItem == null)
            {
                ListBox.SelectedIndex = 0;
            }

            var container = ListBox.ContainerFromIndex(ListBox.SelectedIndex) as Control;
            if (container != null)
            {
                container.Focus(NavigationMethod.Unspecified);
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
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _inputTimer?.Stop();
        _inputTimer = null;

        if (DataContext is IDisposable disposableVm)
        {
            disposableVm.Dispose();
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        bool appIsShuttingDown = (Application.Current as App)?.IsShuttingDown == true;

        if (!_isClosingForReal && !appIsShuttingDown)
        {
            e.Cancel = true;
            HideToTray();
        }

        base.OnClosing(e);
    }
}