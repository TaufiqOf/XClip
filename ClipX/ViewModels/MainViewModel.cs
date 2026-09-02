using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipX.ViewModels;

public enum ClipBoardDataFormat
{
    Text,
    Image,
    File,
    Other
}

public partial class ClipBoardItem : ViewModelBase
{
    public Action<ClipBoardItem>? OnDelete { get; set; }
    [ObservableProperty] private ClipBoardDataFormat _format = ClipBoardDataFormat.Text;
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private DateTime _timestamp = DateTime.Now;

    [RelayCommand]
    private void Delete()
    {
        OnDelete?.Invoke(this);
    }
}

public partial class MainViewModel : ViewModelBase
{
    private string? _lastClipboardText;
    private CancellationTokenSource? _monitorCts;
    private ClipBoardItem? _selectedItem;
    private bool _isAutoStartEnabled;
    private bool _isMonitoringClipboard = true;
    private bool _isInternalSelectionChange;

    public bool IsAutoStartEnabled
    {
        get => _isAutoStartEnabled;
        set
        {
            AutoStartManager.SetEnabled(value);
            SetProperty(ref _isAutoStartEnabled, value);
        }
    }

    public bool IsMonitoringClipboard
    {
        get => _isMonitoringClipboard;
        set
        {
            SetProperty(ref _isMonitoringClipboard, value);
            if (value)
            {
                StartMonitoringClipboard();
            }
            else
            {
                StopMonitoringClipboard();
            }
        }
    }

    public ClipBoardItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!SetProperty(ref _selectedItem, value) || value == null)
                return;

            // Prevent writing to clipboard when selection changed due to incoming clipboard update
            if (_isInternalSelectionChange)
                return;

            _lastClipboardText = value.Text;
            _ = SetClipboardTextAsync(value.Text);
        }
    }

    public ObservableCollection<ClipBoardItem> ClipboardHistory { get; } = new();

    public MainViewModel()
    {
        IsAutoStartEnabled = AutoStartManager.IsEnabled();
        StartMonitoringClipboard();
    }

    [RelayCommand]
    public void ClearHistory()
    {
        ClipboardHistory.Clear();
        SelectedItem = null;
    }

    private IClipboard? GetClipboard()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow?.Clipboard;
        }

        return null;
    }

    private async Task SetClipboardTextAsync(string text)
    {
        var clipboard = GetClipboard();
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private void StartMonitoringClipboard()
    {
        StopMonitoringClipboard();
        _monitorCts = new CancellationTokenSource();
        _ = MonitorClipboardAsync(_monitorCts.Token);
    }

    private void StopMonitoringClipboard()
    {
        _monitorCts?.Cancel();
        _monitorCts?.Dispose();
        _monitorCts = null;
    }

    private async Task MonitorClipboardAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));

        while (!cancellationToken.IsCancellationRequested && await timer.WaitForNextTickAsync(cancellationToken))
        {
            // Always query the clipboard on the UI thread
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var clipboard = GetClipboard();
                if (clipboard == null) return;

                try
                {
                    string? text = await clipboard.TryGetTextAsync();

                    if (!string.IsNullOrWhiteSpace(text) && text != _lastClipboardText)
                    {
                        _lastClipboardText = text;

                        var existingItem = ClipboardHistory.FirstOrDefault(i => i.Text == text);

                        if (existingItem != null)
                        {
                            // Move existing item to top
                            ClipboardHistory.Remove(existingItem);
                            ClipboardHistory.Insert(0, existingItem);

                            _isInternalSelectionChange = true;
                            SelectedItem = existingItem;
                            _isInternalSelectionChange = false;
                        }
                        else
                        {
                            // Add new item to top
                            var newItem = new ClipBoardItem
                            {
                                Text = text,
                                Format = ClipBoardDataFormat.Text,
                                Timestamp = DateTime.Now
                            };
                            newItem.OnDelete += OnDelete;

                            ClipboardHistory.Insert(0, newItem);

                            _isInternalSelectionChange = true;
                            SelectedItem = newItem;
                            _isInternalSelectionChange = false;
                        }
                    }
                }
                catch
                {
                    // Ignore transient locks when external applications update clipboard
                }
            });
        }
    }

    private void OnDelete(ClipBoardItem obj)
    {
        obj.OnDelete -= OnDelete;
        ClipboardHistory.Remove(obj);

        if (SelectedItem == obj)
        {
            SelectedItem = ClipboardHistory.FirstOrDefault();
        }
    }

    [RelayCommand]
    private async Task CopyAsync(ClipBoardItem? item)
    {
        var targetItem = item ?? SelectedItem;
        if (targetItem == null) return;

        _lastClipboardText = targetItem.Text;
        await SetClipboardTextAsync(targetItem.Text);

        _isInternalSelectionChange = true;
        SelectedItem = targetItem;
        _isInternalSelectionChange = false;
    }

    [RelayCommand]
    private async Task ClearClipboardAsync()
    {
        _lastClipboardText = null;

        _isInternalSelectionChange = true;
        SelectedItem = null;
        _isInternalSelectionChange = false;

        var clipboard = GetClipboard();
        if (clipboard != null)
        {
            await clipboard.ClearAsync();
            await clipboard.SetTextAsync("");
        }
    }
}