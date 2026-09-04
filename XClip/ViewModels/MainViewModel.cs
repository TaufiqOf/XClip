using System;
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

    private readonly GlobalHotkeyService? _hotkeyService;
    private bool _isInternalSelectionChange;
    private bool _isMonitoringClipboard = true;
    private string? _lastClipboardSignature;
    private string? _lastClipboardText;
    private DateTime _lastImageHashCheckUtc = DateTime.MinValue;
    private string? _lastImageMetaSignature;
    private CancellationTokenSource? _monitorCts;
    private string _searchText = string.Empty;

    public MainViewModel(GlobalHotkeyService hotkeyService)
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

            _lastClipboardText = value.Text;
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

    private async Task SetClipboardTextAsync(string text)
    {
        var clipboard = GetClipboard();
        if (clipboard != null) await clipboard.SetTextAsync(text);
    }

    private async Task SetClipboardItemAsync(Models.ClipBoardItem item)
    {
        var clipboard = GetClipboard();
        if (clipboard == null)
            return;

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
        Bitmap? imageData = null)
    {
        if (signature == _lastClipboardSignature) return;

        _lastClipboardSignature = signature;
        _lastClipboardText = text;

        var existingItem = ClipboardHistory.FirstOrDefault(i => i.Signature == signature);

        if (existingItem != null)
        {
            ClipboardHistory.Remove(existingItem);
            ClipboardHistory.Insert(0, existingItem);

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
            ImageData = imageData
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
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var clipboard = GetClipboard();
                if (clipboard == null) return;

                try
                {
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
            });
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

        _lastClipboardText = targetItem.Text;
        _lastClipboardSignature = targetItem.Signature;
        await SetClipboardItemAsync(targetItem);

        _isInternalSelectionChange = true;
        SelectedItem = targetItem;
        _isInternalSelectionChange = false;
    }

    [RelayCommand]
    private async Task ClearClipboardAsync()
    {
        _lastClipboardText = null;
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
            // Fuzzy search and sort by match ratio above 40% threshold
            var matches = ClipboardHistory
                .Select(item => new
                {
                    Item = item,
                    Score = Fuzz.PartialRatio(SearchText.ToLower(), item.Text.ToLower())
                })
                .Where(x => x.Score > 40)
                .OrderByDescending(x => x.Score)
                .Select(x => x.Item);

            foreach (var item in matches) FilteredHistory.Add(item);
        }

        UpdateIndexes();

        // Auto select first match if present
        _isInternalSelectionChange = true;
        SelectedItem = FilteredHistory.FirstOrDefault();
        _isInternalSelectionChange = false;
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