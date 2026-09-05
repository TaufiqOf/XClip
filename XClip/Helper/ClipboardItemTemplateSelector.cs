using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Metadata;
using XClip.Models;

namespace XClip.Helper;

public class ClipboardItemTemplateSelector : IDataTemplate
{
    // Allows defining DataTemplates directly inside XAML dictionary
    [Content]
    public Dictionary<ClipboardDataFormat, IDataTemplate> Templates { get; } = new();

    public Control? Build(object? param)
    {
        if (param is ClipboardItem item && Templates.TryGetValue(item.Format, out var template))
        {
            return template.Build(param);
        }

        return null;
    }

    public bool Match(object? data)
    {
        return data is ClipboardItem item && Templates.ContainsKey(item.Format);
    }
}