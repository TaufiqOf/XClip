using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using XClip.Models;

namespace XClip.Services;

internal abstract class AClipboardService()
{
    protected static IClipboard? GetClipboard()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow?.Clipboard;

        return null;
    }
    public abstract Task<ClipboardItem?> GetDataAsync();
    public abstract Task CreateSignature(ClipboardItem item);
    public abstract Task CopyData(ClipboardItem value);
    public abstract object GetClipboardData();
}