using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using AvaloniaEdit.Utils;
using ScratchClip.Models;
using ImageClipboardItem = ScratchClip.Models.ImageClipboardItem;

namespace ScratchClip.Manager;

public static class ClipboardHistoryManager
{
    private static readonly string FolderPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ScratchClip");

    private static readonly string FilePath = Path.Combine(FolderPath, "history.json");

    public static async Task<IReadOnlyList<AClipboardItem>> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return Array.Empty<AClipboardItem>();

            var json = File.ReadAllText(FilePath);
            var records = JsonSerializer.Deserialize<List<ClipboardHistoryRecord>>(json) ??
                          new List<ClipboardHistoryRecord>();

            var tasks = await Task.WhenAll(records
                .Select(ToClipboardItem)); 
            var items = tasks
                .Where(item => item != null)
                .Cast<AClipboardItem>()
                .ToList();
             
             return items;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load clipboard history: {ex.Message}");
            return Array.Empty<AClipboardItem>();
        }
    }

    public static void Save(IReadOnlyList<AClipboardItem> items)
    {
        try
        {
            Directory.CreateDirectory(FolderPath);
            var records = items
                .Select(ToRecord)
                .Where(record => record != null)
                .Cast<ClipboardHistoryRecord>()
                .ToList();

            var json = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save clipboard history: {ex.Message}");
        }
    }

    private static ClipboardHistoryRecord? ToRecord(AClipboardItem item)
    {
        if (item is TextClipboardItem textItem)
        {
            return new ClipboardHistoryRecord
            {
                Format = ClipboardDataFormat.Text,
                Text = textItem.Text,
                DisplayText = textItem.DisplayText,
                Signature = textItem.Signature,
                Timestamp = textItem.Timestamp,
                MataData = textItem.MataData!,
                Tags = textItem.Tags.ToList()
            };
        }

        if (item is ImageClipboardItem imageItem && imageItem.Image != null)
        {
            using var stream = new MemoryStream();
            imageItem.Image.Save(
                stream,
                PngBitmapEncoderOptions.Default);
            return new ClipboardHistoryRecord
            {
                Format = ClipboardDataFormat.Image,
                DisplayText = imageItem.DisplayText,
                Text = imageItem.Text,
                Signature = imageItem.Signature,
                Timestamp = imageItem.Timestamp,
                ImageBase64 = Convert.ToBase64String(stream.ToArray()),
                MataData = imageItem.MataData!,
                Tags = imageItem.Tags.ToList()
            };
        }

        if (item is StorageClipboardItem storageItem)
        {
            return new ClipboardHistoryRecord
            {
                Format = ClipboardDataFormat.Storage,
                DisplayText = storageItem.DisplayText,
                Signature = storageItem.Signature,
                Timestamp = storageItem.Timestamp,
                Files = storageItem.Files,
                Folders = storageItem.Folders,
                MataData = storageItem.MataData!,
                Tags = storageItem.Tags.ToList()
            };
        }

        return null;
    }

    private static async Task<AClipboardItem?> ToClipboardItem(ClipboardHistoryRecord record)
    {
        switch (record.Format)
        {
            case ClipboardDataFormat.Text:
            {
                var text = record.Text ?? string.Empty;
                var item = new TextClipboardItem
                {
                    Format = ClipboardDataFormat.Text,
                    Text = text,
                    DisplayText = string.IsNullOrWhiteSpace(record.DisplayText) ? text : record.DisplayText,
                    Signature = string.IsNullOrWhiteSpace(record.Signature) ? HashText(text) : record.Signature,
                    Timestamp = record.Timestamp == default ? DateTime.Now : record.Timestamp,
                    MataData = record.MataData!,
                };
                item.Tags.Clear();
                item.Tags.AddRange(record.Tags ?? new List<string>());
                item.UpdateByTags();
                return item;
            }
            case ClipboardDataFormat.Image:
            {
                if (string.IsNullOrWhiteSpace(record.ImageBase64))
                    return null;

                var bytes = Convert.FromBase64String(record.ImageBase64);
                using var stream = new MemoryStream(bytes);

                var imageClipboardItem = new ImageClipboardItem
                {
                    Format = ClipboardDataFormat.Image,
                    Text = record.Text ?? string.Empty,
                    DisplayText = string.IsNullOrWhiteSpace(record.DisplayText) ? "Image" : record.DisplayText,
                    Signature = record.Signature ?? string.Empty,
                    Timestamp = record.Timestamp == default ? DateTime.Now : record.Timestamp,
                    Image = new Bitmap(stream),
                    MataData = record.MataData!
                };
                imageClipboardItem.Tags.Clear();
                imageClipboardItem.Tags.AddRange(record.Tags ?? new List<string>());
                return imageClipboardItem;
            }
            case ClipboardDataFormat.Storage:
            {
                if (string.IsNullOrWhiteSpace(record.ImageBase64))
                    return null;

                var bytes = Convert.FromBase64String(record.ImageBase64);
                using var stream = new MemoryStream(bytes);

                var storageClipboardItem = new StorageClipboardItem
                {
                    Format = ClipboardDataFormat.Storage,
                    Text = record.Text ?? string.Empty,
                    DisplayText = string.IsNullOrWhiteSpace(record.DisplayText) ? "Storage" : record.DisplayText,
                    Signature = record.Signature ?? string.Empty,
                    Timestamp = record.Timestamp == default ? DateTime.Now : record.Timestamp,
                    MataData = record.MataData!
                };
                storageClipboardItem.Files.AddRange(record.Files ?? new List<string>());
                storageClipboardItem.Folders.AddRange(record.Folders ?? new List<string>());
                storageClipboardItem.Tags.Clear();
                storageClipboardItem.Tags.AddRange(record.Tags ?? new List<string>());

                return storageClipboardItem;
            }
            default:
                return null;
        }
    }

    private static string HashText(string text)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private sealed class ClipboardHistoryRecord
    {
        public ClipboardDataFormat Format { get; set; }
        public string? Text { get; set; }
        public string? DisplayText { get; set; }
        public string? Signature { get; set; }
        public DateTime Timestamp { get; set; }
        public string? ImageBase64 { get; set; }

        public List<string>? Files { get; set; }
        public List<string>? Folders { get; set; }
        public List<string>? MataData { get; set; }
        public List<string>? Tags { get; set; }
    }
}