using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using FuzzySharp;
using XClip.Models;
using XClip.Services;
using XClip.Views;

namespace XClip.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan ImageHashRecheckInterval = TimeSpan.FromSeconds(10);

    private sealed record ClipboardStorageItemInfo(string Path, bool IsFolder);

    private readonly GlobalHotkeyService? _hotkeyService;
    private bool _isInternalSelectionChange;
    private bool _isMonitoringClipboard = true;
    private string? _lastClipboardSignature;
    private DateTime _lastImageHashCheckUtc = DateTime.MinValue;
    private string? _lastImageMetaSignature;
    private CancellationTokenSource? _monitorCts;
    private string _searchText = string.Empty;

    public static ObservableCollection<ClipboardItem> FilteredHistory { get; private set; } = new();

    public MainViewModel(GlobalHotkeyService hotkeyService)
    {
        _hotkeyService = hotkeyService;
        IsAutoStartEnabled = AutoStartManager.IsEnabled();
        ClipboardManager.OnClipboardItemAdded += ClipboardItemAdded;
        ClipboardManager.OnSelectExistingClipboardItem += OnSelectExistingClipboardItem;
        StartMonitoringClipboard();
        
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
        get => _isMonitoringClipboard;
        set
        {
            SetProperty(ref _isMonitoringClipboard, value);
            if (value)
                StartMonitoringClipboard();
            else
                StopMonitoringClipboard();
        }
    }

    public Models.ClipboardItem? SelectedItem
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
    
    private void OnSelectExistingClipboardItem(ClipboardItem clipboardItem)
    {
        SelectedItem = clipboardItem;
    }

    private void ClipboardItemAdded(ClipboardItem clipboardItem)
    {
        clipboardItem.DisplayIndex = FilteredHistory.Count + 1;
        FilteredHistory.Insert(0, clipboardItem);
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

    [RelayCommand]
    private async Task DoubleClickAsync()
    {
        await CopyAsync(SelectedItem);
    }

    [RelayCommand]
    private async Task CopyAsync(Models.ClipboardItem? item)
    {
        var targetItem = item ?? SelectedItem;
        if (targetItem == null) return;

        _lastClipboardSignature = targetItem.Signature;
        await SetClipboardItemAsync(targetItem);

        _isInternalSelectionChange = true;
        SelectedItem = targetItem;
        _isInternalSelectionChange = false;
    }

    [RelayCommand]
    private void ClearHistory()
    {
        ClipboardManager.ClearClipboardHistory();
    }

    [RelayCommand]
    private async Task ClearClipboardAsync()
    {
        _lastClipboardSignature = null;
        _lastImageMetaSignature = null;
        _lastImageHashCheckUtc = DateTime.MinValue;

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
    }
}

public static class ClipboardManager
{
    public static Action<ClipboardItem>? OnClipboardItemAdded;
    public static Action<ClipboardItem>? OnSelectExistingClipboardItem;
    private static List<ClipboardItem> ClipboardHistory { get; set; } = new();

    private static readonly Dictionary<ClipboardDataFormat, IClipboardService> _clipboardService;

    static ClipboardManager()
    {
        _clipboardService = new Dictionary<ClipboardDataFormat, IClipboardService>();
        _clipboardService[ClipboardDataFormat.Text] = new TextClipboardService();
    }

    private static IClipboard? GetClipboard()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow?.Clipboard;

        return null;
    }

    public static async Task CheckClipboard()
    {
        try
        {
            var clipboard = GetClipboard();
            if (clipboard == null)
                return;
            var type = await GetDataTypeAsync(clipboard);
            if (type == null)
                return;
            ClipboardItem? item = null;
            item = await GetItemAsync(clipboard, type.Value);
            if (item == null)
                return;
            var existingItem = ClipboardHistory.FirstOrDefault(q => q.Signature == item?.Signature);
            if (existingItem != null)
            {
                OnSelectExistingClipboardItem?.Invoke(existingItem);
            }
            else
            {
                ClipboardHistory.Insert(0, item);
                OnClipboardItemAdded?.Invoke(item);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
       
    }

    public static async Task<ClipboardDataFormat?> GetDataTypeAsync(IClipboard clipboard)
    {
        var data = await clipboard.TryGetDataAsync();
        if (data == null) return null;
        if (data.Formats.Any(q => q == DataFormat.Text)) return ClipboardDataFormat.Text;
        if (data.Formats.Any(q => q == DataFormat.Bitmap)) return ClipboardDataFormat.Image;
        if (data.Formats.Any(q => q == DataFormat.File)) return ClipboardDataFormat.StorageItems;
        return null;
    }

    public static async Task<ClipboardItem?> GetItemAsync(IClipboard clipboard, ClipboardDataFormat type)
    {
        ClipboardItem? item = null;
        switch (type)
        {
            case ClipboardDataFormat.Text:
                item = await _clipboardService[type].GetDataAsync(await clipboard.TryGetTextAsync());
                if (item != null)
                    await _clipboardService[type].CreateSignature(item);
                break;
        }

        return item;
    }


    public static async Task SetClipboardItemAsync(ClipboardItem targetItem)
    {
        var clipboard = GetClipboard();
        if (clipboard == null)
            return;

        switch (targetItem.Format)
        {
            case ClipboardDataFormat.Text:
                await clipboard.SetTextAsync(targetItem.Text);
                break;
        }
    }

    public static void ClearClipboardHistory()
    {
        ClipboardHistory.Clear();
    }

    public static async Task ClearClipboardData()
    {
        var clipboard = GetClipboard();
        if (clipboard != null)
        {
            await clipboard.ClearAsync();
            await clipboard.SetTextAsync("");
        }
    }
}

internal interface IClipboardService
{
    Task<ClipboardItem?> GetDataAsync(string? text);
    Task CreateSignature(ClipboardItem item);
}

internal class TextClipboardService : IClipboardService
{
    public async Task<ClipboardItem?> GetDataAsync(string? text)
    {
        ClipboardItem? item = null;
        var testEmpty = StripText(text);
        if (string.IsNullOrEmpty(testEmpty)) return await Task.FromResult(item);

        item = new ClipboardItem
        {
            Format = ClipboardDataFormat.Text,
            Text = text,
            Timestamp = DateTime.Now
        };

        return await Task.FromResult(item);
    }

    public Task CreateSignature(ClipboardItem item)
    {
        item.Signature = item.Text.GetHashCode().ToString();
        return Task.CompletedTask;
    }

    private string? StripText(string? text)
    {
        return text?.Trim().Replace("\r", "").Replace("\n", "").Replace("\t", "");
    }
}