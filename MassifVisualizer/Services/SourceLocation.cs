using System;
using System.IO;

namespace MassifVisualizer.Services;

public readonly record struct SourceLocation(string FileName, int Line)
{
    public static SourceLocation? Parse(string? site)
    {
        if (string.IsNullOrWhiteSpace(site)) return null;

        var text = site.Trim();

        int close = text.LastIndexOf(')');
        int open = close > 0 ? text.LastIndexOf('(', close) : -1;
        if (open >= 0)
            text = text[(open + 1)..close];

        int colon = text.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(text[(colon + 1)..], out int line) || line <= 0)
            return null;

        return new SourceLocation(text[..colon].Trim(), line);
    }

    public bool IsIn(string sourcePath) =>
        string.Equals(FileName, Path.GetFileName(sourcePath), StringComparison.OrdinalIgnoreCase);
}
