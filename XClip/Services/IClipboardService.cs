using System.Threading.Tasks;
using XClip.Models;

namespace XClip.Services;

internal interface IClipboardService
{
    Task<ClipboardItem?> GetDataAsync(string? text);
    Task CreateSignature(ClipboardItem item);
}