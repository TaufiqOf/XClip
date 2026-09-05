using System;
using System.Threading.Tasks;
using XClip.Models;

namespace XClip.Services;

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