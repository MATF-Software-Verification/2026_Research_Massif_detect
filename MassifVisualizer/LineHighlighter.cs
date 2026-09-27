using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Rendering;

namespace MassifVisualizer;

public class LineHighlighter : IBackgroundRenderer
{
    private readonly Dictionary<int, IBrush> _lines = new();

    public KnownLayer Layer => KnownLayer.Background;

    public void Add(int line, IBrush brush) => _lines[line] = brush;

    public void Draw(TextView textView, DrawingContext context)
    {
        if (_lines.Count == 0 || textView.Document == null) return;

        textView.EnsureVisualLines();

        foreach (var (line, brush) in _lines)
        {
            if (line < 1 || line > textView.Document.LineCount) continue;

            var docLine = textView.Document.GetLineByNumber(line);
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, docLine))
                context.FillRectangle(brush, new Rect(0, rect.Y, textView.Bounds.Width, rect.Height));
        }
    }
}
