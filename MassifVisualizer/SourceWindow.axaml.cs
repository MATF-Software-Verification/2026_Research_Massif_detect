using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.Highlighting;

namespace MassifVisualizer;

public partial class SourceWindow : Window
{
    private static readonly IBrush AllocationBrush = new SolidColorBrush(Color.Parse("#FFE0B2"));
    private static readonly IBrush CallerBrush     = new SolidColorBrush(Color.Parse("#CFE3FF"));

    public SourceWindow()
    {
        InitializeComponent();
    }

    public SourceWindow(string path, string caption,
                        IReadOnlyList<int> allocationLines,
                        IReadOnlyList<int> callerLines) : this()
    {
        Title = Path.GetFileName(path);
        CaptionText.Text = caption;
        PathText.Text = path;

        AllocationSwatch.Background = AllocationBrush;
        CallerSwatch.Background = CallerBrush;

        Editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C++");

        try
        {
            Editor.Text = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            Editor.Text = $"Could not read {path}\n\n{ex.Message}";
            PathText.Text = $"Error: {ex.Message}";
            return;
        }

        AllocationLegend.IsVisible = allocationLines.Count > 0;
        CallerLegend.IsVisible = callerLines.Count > 0;

        var highlighter = new LineHighlighter();
        foreach (var line in callerLines)
            highlighter.Add(line, CallerBrush);
        foreach (var line in allocationLines)
            highlighter.Add(line, AllocationBrush);

        Editor.TextArea.TextView.BackgroundRenderers.Add(highlighter);

        var first = allocationLines.Concat(callerLines).DefaultIfEmpty(0).Min();
        if (first > 0)
            Editor.ScrollToLine(first);
    }
}
