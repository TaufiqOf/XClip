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

public partial class MainViewModelCopy : ViewModelBase, IDisposable
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

    public MainViewModelCopy(GlobalHotkeyService hotkeyService)
    {
        _hotkeyService = hotkeyService;
        IsAutoStartEnabled = AutoStartManager.IsEnabled();
        StartMonitoringClipboard();
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value)) ApplyFilter();
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

    public Models.ClipBoardItem? SelectedItem
    {
        get;
        set
        {
            if (!SetProperty(ref field, value) || value == null)
                return;

            if (_isInternalSelectionChange)
                return;

            _lastClipboardSignature = value.Signature;
            _ = SetClipboardItemAsync(value);
        }
    }

    public ObservableCollection<Models.ClipBoardItem> ClipboardHistory { get; } = new();
    public ObservableCollection<Models.ClipBoardItem> FilteredHistory { get; } = new();

    public void Dispose()
    {
        StopMonitoringClipboard();
    }

    [RelayCommand]
    public void ClearHistory()
    {
        ClipboardHistory.Clear();
        SelectedItem = null;
        UpdateIndexesAndFilter();
    }

    private IClipboard? GetClipboard()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow?.Clipboard;

        return null;
    }

    private IStorageProvider? GetStorageProvider()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow?.StorageProvider;

        return null;
    }

    private async Task SetClipboardItemAsync(Models.ClipBoardItem item)
    {
        var clipboard = GetClipboard();
        if (clipboard == null)
            return;

        if (item.StorageItemPaths.Count > 0)
        {
            var storageProvider = GetStorageProvider();
            if (storageProvider != null)
            {
                var storageItems = new List<IStorageItem>();

                foreach (var path in item.StorageItemPaths.Distinct(StringComparer.Ordinal))
                {
                    IStorageItem? storageItem = null;

                    if (Directory.Exists(path))
                        storageItem = await storageProvider.TryGetFolderFromPathAsync(path);
                    else if (File.Exists(path))
                        storageItem = await storageProvider.TryGetFileFromPathAsync(path);

                    if (storageItem != null)
                        storageItems.Add(storageItem);
                }

                if (storageItems.Count > 0)
                {
                    await clipboard.SetFilesAsync(storageItems);
                    return;
                }
            }

            await clipboard.SetTextAsync(string.Join(Environment.NewLine, item.StorageItemPaths));
            return;
        }

        if (item.Format == ClipBoardDataFormat.Image && item.ImageData != null)
        {
            await clipboard.SetBitmapAsync(item.ImageData);
            return;
        }

        await clipboard.SetTextAsync(item.Text);
    }

    private static string ComputeImageSignature(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        var hash = SHA256.HashData(stream);
        return $"image:{Convert.ToHexString(hash)}";
    }

    private static string GetStorageItemPath(IStorageItem item)
    {
        var localPath = item.TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(localPath))
            return localPath;

        return item.Path.IsFile ? item.Path.LocalPath : item.Path.ToString();
    }

    private static string BuildStorageItemsSignature(IReadOnlyList<ClipboardStorageItemInfo> items)
    {
        return $"storage:{string.Join("|", items.OrderBy(i => i.Path, StringComparer.Ordinal)
            .Select(i => $"{(i.IsFolder ? "folder" : "file")}:{i.Path}"))}";
    }

    private static ClipBoardDataFormat GetStorageDataFormat(IReadOnlyList<ClipboardStorageItemInfo> items)
    {
        if (items.All(i => i.IsFolder))
            return ClipBoardDataFormat.Folder;

        if (items.All(i => !i.IsFolder))
            return ClipBoardDataFormat.File;

        return ClipBoardDataFormat.Other;
    }

    private static string BuildStorageItemsText(IReadOnlyList<ClipboardStorageItemInfo> items)
    {
        var label = items.Count switch
        {
            1 when items[0].IsFolder => "[Folder]",
            1 => "[File]",
            _ when items.All(i => i.IsFolder) => $"[{items.Count} Folders]",
            _ when items.All(i => !i.IsFolder) => $"[{items.Count} Files]",
            _ => $"[{items.Count} Items]"
        };

        return string.Join(Environment.NewLine, new[] { label }.Concat(items.Select(i => i.Path)));
    }

    private static List<ClipboardStorageItemInfo> ExtractStorageItemInfos(IEnumerable<IStorageItem> storageItems)
    {
        return storageItems
            .Select(item => new ClipboardStorageItemInfo(GetStorageItemPath(item), item is IStorageFolder))
            .Where(item => !string.IsNullOrWhiteSpace(item.Path))
            .DistinctBy(item => item.Path, StringComparer.Ordinal)
            .ToList();
    }

    private bool ShouldSkipImageHash(Bitmap bitmap)
    {
        var metaSignature = $"{bitmap.PixelSize.Width}x{bitmap.PixelSize.Height}:{bitmap.Dpi.X:F2}:{bitmap.Dpi.Y:F2}";
        var isSameMeta = string.Equals(metaSignature, _lastImageMetaSignature, StringComparison.Ordinal);
        var isRecent = DateTime.UtcNow - _lastImageHashCheckUtc < ImageHashRecheckInterval;

        if (isSameMeta && isRecent &&
            _lastClipboardSignature?.StartsWith("image:", StringComparison.Ordinal) == true) return true;

        _lastImageMetaSignature = metaSignature;
        _lastImageHashCheckUtc = DateTime.UtcNow;
        return false;
    }

    private void UpsertClipboardItem(string signature, string text, ClipBoardDataFormat format,
        Bitmap? imageData = null, IReadOnlyList<string>? storageItemPaths = null)
    {
        if (signature == _lastClipboardSignature) return;

        _lastClipboardSignature = signature;
        var existingItem = ClipboardHistory.FirstOrDefault(i => i.Signature == signature);

        if (existingItem != null)
        {
            _isInternalSelectionChange = true;
            SelectedItem = existingItem;
            _isInternalSelectionChange = false;
            return;
        }

        var newItem = new Models.ClipBoardItem
        {
            Text = text,
            Format = format,
            Timestamp = DateTime.Now,
            DisplayIndex = ClipboardHistory.Count + 1,
            Signature = signature,
            ImageData = imageData,
            StorageItemPaths = storageItemPaths ?? Array.Empty<string>()
        };
        newItem.OnDelete += OnDelete;

        ClipboardHistory.Insert(0, newItem);

        _isInternalSelectionChange = true;
        SelectedItem = newItem;
        _isInternalSelectionChange = false;
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
            await Dispatcher.UIThread.InvokeAsync(() => PollClipboardAsync())
                ;
    }

    private async Task PollClipboardAsync()
    {
        var clipboard = GetClipboard();
        if (clipboard == null) return;

        try
        {
            var storageItems = await clipboard.TryGetFilesAsync();
            if (storageItems is { Length: > 0 })
            {
                var storageInfos = ExtractStorageItemInfos(storageItems);
                if (storageInfos.Count > 0)
                {
                    var storageSignature = BuildStorageItemsSignature(storageInfos);
                    if (storageSignature == _lastClipboardSignature) return;

                    UpsertClipboardItem(
                        storageSignature,
                        BuildStorageItemsText(storageInfos),
                        GetStorageDataFormat(storageInfos),
                        storageItemPaths: storageInfos.Select(i => i.Path).ToArray());
                    UpdateIndexesAndFilter();
                    return;
                }
            }

            var text = await clipboard.TryGetTextAsync();

            if (!string.IsNullOrWhiteSpace(text))
            {
                var textSignature = $"text:{text}";
                if (textSignature == _lastClipboardSignature) return;
                UpsertClipboardItem(textSignature, text, ClipBoardDataFormat.Text);
                UpdateIndexesAndFilter();
                return;
            }

            var bitmap = await clipboard.TryGetBitmapAsync();
            if (bitmap != null)
            {
                if (ShouldSkipImageHash(bitmap)) return;

                var imageSignature = ComputeImageSignature(bitmap);
                var imageLabel = $"[Image] {bitmap.PixelSize.Width}x{bitmap.PixelSize.Height}";
                if (imageSignature == _lastClipboardSignature) return;
                UpsertClipboardItem(imageSignature, imageLabel, ClipBoardDataFormat.Image, bitmap);
                UpdateIndexesAndFilter();
            }
        }
        catch
        {
            // Ignore transient locks when external applications update clipboard
        }
    }

    private void OnDelete(Models.ClipBoardItem obj)
    {
        obj.OnDelete -= OnDelete;
        ClipboardHistory.Remove(obj);

        if (SelectedItem == obj) SelectedItem = FilteredHistory.FirstOrDefault();

        UpdateIndexesAndFilter();
    }

    [RelayCommand]
    public async Task CopyAsync(Models.ClipBoardItem? item)
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
    private async Task ClearClipboardAsync()
    {
        _lastClipboardSignature = null;
        _lastImageMetaSignature = null;
        _lastImageHashCheckUtc = DateTime.MinValue;

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

    public void SelectAndPasteByIndex(int index)
    {
        if (index >= 0 && index < FilteredHistory.Count) SelectedItem = FilteredHistory[index];
    }

    private void ApplyFilter()
    {
        FilteredHistory.Clear();

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            foreach (var item in ClipboardHistory) FilteredHistory.Add(item);
        }
        else
        {
            // Fuzzy search and sort by match ratio above 60% threshold
            var matches = ClipboardHistory
                .Select(item => new
                {
                    Item = item,
                    Score = Fuzz.PartialRatio(SearchText.ToLower(), item.Text.ToLower())
                })
                .Where(x => x.Score > 60)
                .OrderByDescending(x => x.Score)
                .Select(x => x.Item);

            foreach (var item in matches) FilteredHistory.Add(item);
        }

        UpdateIndexes();

        // Auto select first match if present
    }

    private void UpdateIndexesAndFilter()
    {
        ApplyFilter();
    }

    private void UpdateIndexes()
    {
        var i = 1;
        foreach (var clipBoardItem in FilteredHistory.OrderBy(q => q.Timestamp).ToList())
            clipBoardItem.DisplayIndex = i++;
    }
}