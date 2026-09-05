using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using FuzzySharp;
using XClip.Manager;
using XClip.Models;
using XClip.Services;
using XClip.Views;

namespace XClip.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    public Action? OnHideToTray;
    private readonly GlobalHotkeyService _hotkeyService;
    private bool _isInternalSelectionChange;
    public string? LastClipboardSignature { get; private set; }
    public DateTime LastImageHashCheckUtc { get; private set; } = DateTime.MinValue;
    private string? _lastImageMetaSignature;
    private CancellationTokenSource? _monitorCts;
    private string _searchText = string.Empty;
    private readonly System.Timers.Timer _searchDebounceTimer;
    private string registerNumber = string.Empty;
    public ObservableCollection<ClipboardItem> FilteredHistory { get; private set; } = new();

    public MainViewModel(GlobalHotkeyService hotkeyService)
    {
        _hotkeyService = hotkeyService;
        IsAutoStartEnabled = AutoStartManager.IsEnabled();
        ClipboardManager.OnClipboardItemAdded += OnClipboardItemAdded;
        ClipboardManager.OnSelectExistingClipboardItem += OnSelectExistingClipboardItem;
        ClipboardManager.OnRemoveExistingClipboardItem += OnRemoveExistingClipboardItem;
        StartMonitoringClipboard();
        _searchDebounceTimer = new System.Timers.Timer(300);
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Elapsed += SearchDebounceTimerOnElapsed;
    }


    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
            }
        }
    }

    public bool IsAutoStartEnabled
    {
        get;
        set
        {
            AutoStartManager.SetEnabled(value);
            SetProperty(ref field, value);
        }
    }

    public bool IsMonitoringClipboard
    {
        get;
        set
        {
            SetProperty(ref field, value);
            if (value)
                StartMonitoringClipboard();
            else
                StopMonitoringClipboard();
        }
    } = true;

    public ClipboardItem? SelectedItem
    {
        get;
        set
        {
            if (!SetProperty(ref field, value) || value == null)
                return;

            if (_isInternalSelectionChange)
                return;
            _ = SetClipboardItemAsync(value);
        }
    }

    public async Task SimulatePasteAsync()
    {
        await _hotkeyService.SimulatePasteAsync();
    }

    public void Dispose()
    {
        StopMonitoringClipboard();
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

    private void OnClipboardItemAdded(ClipboardItem clipboardItem)
    {
        clipboardItem.DisplayIndex = FilteredHistory.Count + 1;
        FilteredHistory.Insert(0, clipboardItem);
    }

    private void OnSelectExistingClipboardItem(ClipboardItem clipboardItem)
    {
        SelectedItem = clipboardItem;
    }

    private void OnRemoveExistingClipboardItem(ClipboardItem obj)
    {
        FilteredHistory.Remove(obj);
    }


    private async Task MonitorClipboardAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));

        while (!cancellationToken.IsCancellationRequested && await timer.WaitForNextTickAsync(cancellationToken))
            await Dispatcher.UIThread.InvokeAsync(() => _ = ClipboardManager.CheckClipboard());
    }


    private void AddToFilteredHistory(ClipboardItem item)
    {
        // Apply filter to determine if the item should be added to FilteredHistory
        if (string.IsNullOrWhiteSpace(SearchText) || Fuzz.PartialRatio(SearchText, item.DisplayText) > 70)
        {
            FilteredHistory.Insert(0, item);
        }
    }

    public async Task DoubleClickAsync()
    {
        await CopyAsync(SelectedItem);
    }

    [RelayCommand]
    private async Task CopyAsync(Models.ClipboardItem? item)
    {
        var targetItem = item ?? SelectedItem;
        if (targetItem == null) return;

        LastClipboardSignature = targetItem.Signature;
        await SetClipboardItemAsync(targetItem);

        _isInternalSelectionChange = true;
        SelectedItem = targetItem;
        _isInternalSelectionChange = false;
    }

    [RelayCommand]
    private void ClearHistory()
    {
        ClipboardManager.ClearClipboardHistory();
        FilteredHistory.Clear();
    }

    [RelayCommand]
    private async Task ClearClipboardAsync()
    {
        LastClipboardSignature = null;
        _lastImageMetaSignature = null;
        LastImageHashCheckUtc = DateTime.MinValue;

        _isInternalSelectionChange = true;
        SelectedItem = null;
        _isInternalSelectionChange = false;
        await ClipboardManager.ClearClipboardData();
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        if (_hotkeyService == null)
            return;

        var settingsVm = new SettingsViewModel(_hotkeyService);
        var settingsWindow = new SettingsWindow { DataContext = settingsVm };

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            if (desktop?.MainWindow != null)
                await settingsWindow.ShowDialog(desktop.MainWindow);
    }


    private async Task SetClipboardItemAsync(ClipboardItem targetItem)
    {
        await ClipboardManager.SetClipboardItemAsync(targetItem);
    }


    public void OnWindowKeyDown(KeyEventArgs keyEventArgs)
    {
        int? number = keyEventArgs.Key switch
        {
            >= Key.D1 and <= Key.D9 => (int)keyEventArgs.Key - (int)Key.D1 + 1,
            >= Key.NumPad1 and <= Key.NumPad9 => (int)keyEventArgs.Key - (int)Key.NumPad1 + 1,
            _ => null
        };

        if (number.HasValue)
        {
            registerNumber += number.Value; // Or number.Value.ToString() if registerNumber is a string
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }
    }


    private async void SearchDebounceTimerOnElapsed(object? sender, ElapsedEventArgs e)
    {
        _searchDebounceTimer.Stop();
        Dispatcher.UIThread.Post(() =>
        {
            _ = FastKeyExecute(); // Fire-and-forget safely or handle exceptions inside FastKeyExecute
        }, DispatcherPriority.Background);
    }

    private async Task FastKeyExecute()
    {
        if (!string.IsNullOrEmpty(registerNumber))
        {
            var item = FilteredHistory.FirstOrDefault(q => q.DisplayIndex == int.Parse(registerNumber));
            if (item != null)
            {
                ClipboardManager.SelectedClipboardItem = item;
            }
            OnHideToTray?.Invoke();
            await _hotkeyService.SimulatePasteAsync();

            registerNumber = string.Empty;
        }
    }
}