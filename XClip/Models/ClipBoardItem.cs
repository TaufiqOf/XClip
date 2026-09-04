using System;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XClip.ViewModels;

namespace XClip.Models;

public partial class ClipBoardItem : ViewModelBase
{
    [ObservableProperty] private int _displayIndex;
    [ObservableProperty] private ClipBoardDataFormat _format = ClipBoardDataFormat.Text;
    [ObservableProperty] private Bitmap? _imageData;
    [ObservableProperty] private string _signature = string.Empty;
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private DateTime _timestamp = DateTime.Now;
    public Action<ClipBoardItem>? OnDelete { get; set; }

    [RelayCommand]
    private void Delete()
    {
        OnDelete?.Invoke(this);
    }
}