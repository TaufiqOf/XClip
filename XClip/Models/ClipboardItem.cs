using System;
using System.Collections.Generic;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XClip.ViewModels;

namespace XClip.Models;

public partial class ClipboardItem : ViewModelBase
{
    [ObservableProperty] private int _displayIndex;
    [ObservableProperty] private ClipboardDataFormat _format = ClipboardDataFormat.Text;
    [ObservableProperty] private Bitmap? _imageData;
    [ObservableProperty] private string _signature = string.Empty;
    [ObservableProperty] private string _displayText = string.Empty;

    [ObservableProperty] private DateTime _timestamp = DateTime.Now;
    private string _text = string.Empty;

    public IReadOnlyList<string> StorageItemPaths { get; set; } = Array.Empty<string>();
    public Action<ClipboardItem>? OnDelete { get; set; }
    public Action<ClipboardItem>? OnDoubleClick;

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value)
                return;

            _text = value;
            OnPropertyChanged();

            var displayText = value.Trim();
            displayText = displayText.Length > 100 ? displayText.Substring(0, 100) + "..." : displayText;
            DisplayText = displayText;
            OnPropertyChanged(nameof(DisplayText));
        }
    }

    [RelayCommand]
    public void DoubleClickCommand()
    {
        OnDoubleClick?.Invoke(this);
    }


    [RelayCommand]
    private void Delete()
    {
        OnDelete?.Invoke(this);
    }
}