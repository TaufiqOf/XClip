using System.Threading.Tasks;
using Avalonia.Input.Platform;
using XClip.Models;

namespace XClip.Services;

internal abstract class AClipboardService()
{
    public abstract Task<ClipboardItem?> GetDataAsync(string? text);
    public abstract Task CreateSignature(ClipboardItem item);
    public abstract Task CopyData(ClipboardItem value);
}