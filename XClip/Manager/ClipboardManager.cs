using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using XClip.Models;
using XClip.Services;
using XClip.Services.ClipboardService;

namespace XClip.Manager;

public static class ClipboardManager
{
    public static Action<ClipboardItem>? OnClipboardItemAdded;
    public static Action<ClipboardItem>? OnSelectExistingClipboardItem;
    public static Action<ClipboardItem>? OnRemoveExistingClipboardItem;
    private static List<ClipboardItem> ClipboardHistory { get; set; } = new();

    private static readonly Dictionary<ClipboardDataFormat, AClipboardService> _clipboardServices;
    private static ClipboardItem? _selectedClipboardItem;
    private static IClipboard? GetClipboard()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow?.Clipboard;

        return null;
    }
    public static ClipboardItem? SelectedClipboardItem
    {
        get => _selectedClipboardItem;
        set
        {
            if (value != null && ClipboardHistory.Contains(value))
            {
                _selectedClipboardItem = value;
                _clipboardServices[value.Format].CopyData(value);
                OnSelectExistingClipboardItem?.Invoke(value);
            }
        }
    }

    static ClipboardManager()
    {
        _clipboardServices = new Dictionary<ClipboardDataFormat, AClipboardService>();
        _clipboardServices[ClipboardDataFormat.Text] = new TextClipboardService();
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
            item = await GetItemAsync(type.Value);
            if (item == null)
                return;
            var existingItem = ClipboardHistory.FirstOrDefault(q => q.Signature == item?.Signature);
            if (existingItem != null)
            {
                if (SelectedClipboardItem != existingItem)
                {
                    _selectedClipboardItem = existingItem;
                    OnSelectExistingClipboardItem?.Invoke(existingItem);
                }
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

    private static async Task<ClipboardItem?> GetItemAsync(ClipboardDataFormat type)
    {
        ClipboardItem? item = null;
        item = await _clipboardServices[type].GetDataAsync();
        if (item == null) return item;
        await _clipboardServices[type].CreateSignature(item);
        item.OnDelete += DeleteClipboardItem;
        return item;
    }


    public static async Task SetClipboardItemAsync(ClipboardItem targetItem)
    { 
        await _clipboardServices[targetItem.Format].CopyData(targetItem);
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

    public static async void DeleteClipboardItem(ClipboardItem item)
    {
        if (SelectedClipboardItem == item)
            SelectedClipboardItem = null;
        var clipboard = GetClipboard();
        if (clipboard != null)
        {
            await clipboard.SetTextAsync("");
        }

        ClipboardHistory.Remove(item);
        OnRemoveExistingClipboardItem?.Invoke(item);
    }
}