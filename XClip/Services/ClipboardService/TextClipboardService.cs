using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using XClip.Models;

namespace XClip.Services.ClipboardService;

internal class TextClipboardService : AClipboardService
{
    private readonly IClipboard _clipboard;
    private static IClipboard? GetClipboard()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow?.Clipboard;

        return null;
    }
    public TextClipboardService()
    {

    }
    public override async Task<ClipboardItem?> GetDataAsync(string? text)
    {
        ClipboardItem? item = null;
        if (string.IsNullOrEmpty(text)) return await Task.FromResult(item);
        var testEmpty = StripText(text);
        if (string.IsNullOrEmpty(testEmpty)) return await Task.FromResult(item);
        var displayText = DisplayText(text);
        item = new ClipboardItem
        {
            Format = ClipboardDataFormat.Text,
            Text = text,
            Timestamp = DateTime.Now,
            DisplayText = displayText

        };

        return await Task.FromResult(item);
    }

 

    public override Task CreateSignature(ClipboardItem item)
    {
        item.Signature = item.Text.GetHashCode().ToString();
        return Task.CompletedTask;
    }

    public override Task CopyData(ClipboardItem value)
    {
        var clipboard = GetClipboard();
        return clipboard!.SetTextAsync(value.Text);
    }
    
    private static string DisplayText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // Truncate to 600 characters first if needed
        var truncated = text.Length > 600 ? text.Substring(0, 600) + "..." : text;

        var lines = truncated.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        if (lines.Length == 0)
            return string.Empty;

        // Determine leading whitespace from the first line
        var firstLine = lines[0];
        int leadingWhitespaceLength = 0;
        while (leadingWhitespaceLength < firstLine.Length && char.IsWhiteSpace(firstLine[leadingWhitespaceLength]))
        {
            leadingWhitespaceLength++;
        }

        if (leadingWhitespaceLength == 0)
            return truncated;

        string indentPrefix = firstLine.Substring(0, leadingWhitespaceLength);

        // Remove the exact prefix from each line if it starts with it
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith(indentPrefix))
            {
                lines[i] = lines[i].Substring(indentPrefix.Length);
            }
        }

        return string.Join(Environment.NewLine, lines);
    }
    private string? StripText(string? text)
    {
        return text?.Trim().Replace("\r", "").Replace("\n", "").Replace("\t", "");
    }
}