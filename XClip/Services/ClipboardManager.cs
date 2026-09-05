using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using XClip.Models;
using XClip.ViewModels;

namespace XClip.Services;

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