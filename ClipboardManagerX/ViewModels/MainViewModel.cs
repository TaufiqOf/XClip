using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipboardManagerX.ViewModels;

public enum ClipBoardDataFormat
{
    Text,
    Image,
    File,
    Other
}

public partial class ClipBoardItem : ViewModelBase
{
    [ObservableProperty] private ClipBoardDataFormat _format = ClipBoardDataFormat.Text;
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private DateTime _timestamp = DateTime.Now;

    [RelayCommand]
    private async Task CopyAsync()
    {
        var clipboard = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow?.Clipboard
            : null;

        if (clipboard != null && !string.IsNullOrEmpty(Text))
        {
            await clipboard.SetTextAsync(Text);
        }
    }
}

public partial class MainViewModel : ViewModelBase
{
    private string? _lastClipboardText;
    private CancellationTokenSource? _monitorCts;
    private ClipBoardItem? _selectedItem;

    public ClipBoardItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (value == null)
            {
                SetProperty(ref _selectedItem, null);
                return;
            }

            if (_selectedItem != null && value.Text == _selectedItem.Text)
                return;

            SetProperty(ref _selectedItem, value);
            _ = GetClipboard()?.SetTextAsync(value.Text);
        }
    }


    public ObservableCollection<ClipBoardItem> ClipboardHistory { get; } = new();
    
    [RelayCommand]
    public void ClearHistoryCommand()
    {
        ClipboardHistory.Clear();
        SelectedItem = null;
    }

    public MainViewModel()
    {
        StartMonitoringClipboard();
    }

    private IClipboard? GetClipboard()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow?.Clipboard;
        }

        return null;
    }

    private void StartMonitoringClipboard()
    {
        _monitorCts = new CancellationTokenSource();
        Task.Run(() => MonitorClipboardAsync(_monitorCts.Token));
    }

    private async Task MonitorClipboardAsync(CancellationToken cancellationToken)
    {
        var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));

        while (!cancellationToken.IsCancellationRequested && await timer.WaitForNextTickAsync(cancellationToken))
        {
            var clipboard = GetClipboard();
            if (clipboard == null) continue;

            try
            {
                var dataObject = await clipboard.TryGetDataAsync();
                if (dataObject != null)
                {
                    var dataItem = dataObject.Items.FirstOrDefault(q => q.Formats.Contains(DataFormat.Text));
                    if (dataItem != null)
                    {
                        var text = await dataItem.TryGetTextAsync();

                        if (!string.IsNullOrWhiteSpace(text) && text != _lastClipboardText)
                        {
                            _lastClipboardText = text;

                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                var newItem = new ClipBoardItem
                                {
                                    Text = text,
                                    Format = ClipBoardDataFormat.Text,
                                    Timestamp = DateTime.Now
                                };

                                if (ClipboardHistory.All(i => i.Text != text))
                                {
                                    ClipboardHistory.Insert(0, newItem);
                                    SelectedItem = newItem;
                                }
                            });
                        }

                        if (string.IsNullOrWhiteSpace(text))
                        {
                            SelectedItem = null;
                        }
                        else if (SelectedItem == null && !string.IsNullOrWhiteSpace(text))
                        {
                            var existingItem = ClipboardHistory.FirstOrDefault(i => i.Text == text);
                            if (existingItem != null)
                            {
                                SelectedItem = existingItem;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore transient lock failures when reading system clipboard
            }
        }
    }

    [RelayCommand]
    private async Task CopyAsync(ClipBoardItem? item)
    {
        var targetItem = item ?? SelectedItem;
        if (targetItem == null) return;

        var clipboard = GetClipboard();
        if (clipboard != null)
        {
            _lastClipboardText = targetItem.Text;
            await clipboard.SetTextAsync(targetItem.Text);
            SelectedItem = targetItem;
        }
    }

    [RelayCommand]
    private async Task ClearClipboardAsync()
    {
        // Unselect ListBox selection
        SelectedItem = null;
        _lastClipboardText = null;

        var clipboard = GetClipboard();
        if (clipboard != null)
        {
            await clipboard.ClearAsync();
            await clipboard.SetTextAsync(string.Empty);
        }
    }
}