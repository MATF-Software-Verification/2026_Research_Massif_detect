using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MediaColors = Avalonia.Media.Colors;
using HA = Avalonia.Layout.HorizontalAlignment;
using VA = Avalonia.Layout.VerticalAlignment;
using FW = Avalonia.Media.FontWeight;
using Avalonia.Platform.Storage;
using MassifVisualizer.Models;
using MassifVisualizer.Services;
using ScottPlot;

namespace MassifVisualizer;

public partial class MainWindow : Window
{
    private MassifProfile? _profile;
    private List<HotFunction> _allHotFunctions = [];

    private const double FontTiny   = 10;
    private const double FontSmall  = 11;
    private const double FontNormal = 12;

    private const double Gap4  = 4;
    private const double Gap8  = 8;
    private const double Gap12 = 12;
    private const double Gap16 = 16;

    private const double BadgeWidth  = 30;
    private const double BadgeHeight = 20;

    private static readonly Thickness    CardPadding = new(Gap12, Gap8);
    private static readonly CornerRadius CardCorner  = new(6);
    private static readonly CornerRadius BadgeCorner = new(BadgeHeight / 2);

    private const int ChartMinWidth  = 400;
    private const int ChartMinHeight = 200;
    private const int LabelColumnWidth = 140;

    private const float ExtraLineWidth  = 1.5f;
    private const float HeapLineWidth   = 2.5f;
    private const float PeakMarkerSize  = 12;
    private const float DetailMarkerSize = 8;

    public MainWindow()
    {
        InitializeComponent();
        SetupMenu();
        SnapshotList.SelectionChanged += (_, _) =>
        {
            if (SnapshotList.SelectedItem is MassifSnapshot snap)
                ShowSnapshot(snap);
        };
        ChartBorder.SizeChanged += (_, _) => RefreshChart();
        TopN3.IsCheckedChanged  += (_, _) => { if (TopN3.IsChecked  == true) RenderHotFunctions(); };
        TopN5.IsCheckedChanged  += (_, _) => { if (TopN5.IsChecked  == true) RenderHotFunctions(); };
        TopN10.IsCheckedChanged += (_, _) => { if (TopN10.IsChecked == true) RenderHotFunctions(); };
    }

    public void LoadFileFromArgs(string path) => LoadFile(path);

    private void SetupMenu()
    {
        MenuOpen.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open Massif Output File",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Massif output") { Patterns = new[] { "massif.out.*", "*.massif", "*" } },
                    FilePickerFileTypes.All
                }
            });

            if (files is { Count: > 0 })
                LoadFile(files[0].Path.LocalPath);
        };

        MenuExit.Click += (_, _) => Close();
    }

    private void LoadFile(string path)
    {
        try
        {
            StatusBar.Text = $"Loading {path}...";
            _profile = MassifParser.Parse(path);

            SnapshotList.Items.Clear();
            foreach (var s in _profile.Snapshots)
                SnapshotList.Items.Add(s);

            SummaryText.Text = BuildSummary(_profile);
            RefreshChart();
            RefreshHotFunctions();

            var peak = _profile.PeakSnapshot ?? _profile.Snapshots.LastOrDefault();
            if (peak != null)
                SnapshotList.SelectedItem = peak;

            StatusBar.Text = $"Loaded {_profile.Snapshots.Count} snapshots from {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusBar.Text = $"Error: {ex.Message}";
        }
    }

    private void RefreshChart()
    {
        if (_profile == null || _profile.Snapshots.Count == 0) return;

        int w = Math.Max(ChartMinWidth,  (int)ChartBorder.Bounds.Width);
        int h = Math.Max(ChartMinHeight, (int)ChartBorder.Bounds.Height);

        var pngBytes = BuildPlot(_profile, w, h);
        using var ms = new MemoryStream(pngBytes);
        ChartImage.Source = new Bitmap(ms);
    }

    private static ScottPlot.Color SP(Avalonia.Media.Color c) => new(c.R, c.G, c.B, c.A);

    private static byte[] BuildPlot(MassifProfile profile, int width, int height)
    {
        var plot = new Plot();

        plot.FigureBackground.Color = SP(MediaColors.LightGray);
        plot.DataBackground.Color   = SP(MediaColors.White);
        plot.Axes.Color(SP(MediaColors.Black));
        plot.Grid.MajorLineColor    = SP(MediaColors.Silver);
        plot.Title("Heap Memory Over Time");
        plot.XLabel($"Time ({profile.TimeUnit})");
        plot.YLabel("Memory");

        var snaps = profile.Snapshots;
        double[] times = snaps.Select(s => (double)s.Time).ToArray();
        double[] heap  = snaps.Select(s => (double)s.MemHeapB).ToArray();
        double[] extra = snaps.Select(s => (double)(s.MemHeapB + s.MemHeapExtraB)).ToArray();

        var extraLine = plot.Add.SignalXY(times, extra);
        extraLine.Color      = SP(MediaColors.Coral);
        extraLine.LineWidth  = ExtraLineWidth;
        extraLine.LegendText = "Heap + Admin";

        var heapLine = plot.Add.SignalXY(times, heap);
        heapLine.Color      = SP(MediaColors.SteelBlue);
        heapLine.LineWidth  = HeapLineWidth;
        heapLine.LegendText = "Heap";

        foreach (var s in snaps.Where(s => s.TreeType != TreeType.Empty))
        {
            bool isPeak = s.TreeType == TreeType.Peak;
            var marker = plot.Add.Marker(s.Time, s.MemHeapB);
            marker.Color  = isPeak ? SP(MediaColors.Crimson) : SP(MediaColors.ForestGreen);
            marker.Shape  = isPeak ? MarkerShape.FilledDiamond : MarkerShape.FilledCircle;
            marker.Size   = isPeak ? PeakMarkerSize : DetailMarkerSize;
            if (isPeak) marker.LegendText = "Peak";
        }

        plot.Axes.Left.TickGenerator = new ScottPlot.TickGenerators.NumericAutomatic
        {
            LabelFormatter = v => v switch
            {
                >= 1_048_576 => $"{v / 1_048_576:F1}M",
                >= 1_024     => $"{v / 1_024:F0}K",
                _            => $"{v:F0}B"
            }
        };

        plot.ShowLegend(Alignment.UpperLeft);
        plot.Axes.AutoScale();

        return plot.GetImage(width, height).GetImageBytes();
    }

    private void RefreshHotFunctions()
    {
        if (_profile == null) return;
        _allHotFunctions = HotFunctionAnalyzer.Analyze(_profile);
        RenderHotFunctions();
    }

    private int GetSelectedTopN()
    {
        if (TopN3.IsChecked  == true) return 3;
        if (TopN10.IsChecked == true) return 10;
        return 5;
    }

    private void RenderHotFunctions()
    {
        HotFunctionsPanel.Children.Clear();

        var functions = _allHotFunctions.Take(GetSelectedTopN()).ToList();

        if (functions.Count == 0)
        {
            HotFunctionsHeader.Text = "No detailed snapshot data available.";
            return;
        }

        var peak = _profile?.PeakSnapshot;
        HotFunctionsHeader.Text = $"Peak snapshot #{peak?.Index}  ·  {_allHotFunctions.Count} allocating functions found";

        foreach (var hf in functions)
            HotFunctionsPanel.Children.Add(BuildHotFunctionCard(hf));
    }

    private Control BuildHotFunctionCard(HotFunction hf)
    {
        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(MediaColors.Silver),
            BorderThickness = new Thickness(1),
            CornerRadius = CardCorner,
            Padding = CardPadding
        };

        var panel = new StackPanel { Spacing = 0 };

        var headerRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };

        var badge = new Border
        {
            Background = new SolidColorBrush(MediaColors.SteelBlue),
            CornerRadius = BadgeCorner,
            Width = BadgeWidth,
            Height = BadgeHeight,
            VerticalAlignment = VA.Center,
            Margin = new Thickness(0, 0, Gap8, 0),
            Child = new TextBlock
            {
                Text = $"#{hf.Rank}",
                Foreground = Brushes.White,
                FontSize = FontTiny,
                FontWeight = FW.Bold,
                HorizontalAlignment = HA.Center,
                VerticalAlignment = VA.Center
            }
        };
        Grid.SetColumn(badge, 0);
        headerRow.Children.Add(badge);

        var funcLabel = new TextBlock
        {
            Text = hf.Label,
            FontSize = FontNormal,
            FontWeight = FW.SemiBold,
            VerticalAlignment = VA.Center,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(funcLabel, 1);
        headerRow.Children.Add(funcLabel);

        var bytesLabel = new TextBlock
        {
            Text = $"{hf.TotalDisplay}  ·  {hf.PeakPercentDisplay}",
            FontSize = FontNormal,
            Foreground = new SolidColorBrush(MediaColors.DimGray),
            VerticalAlignment = VA.Center,
            Margin = new Thickness(Gap16, 0, 0, 0)
        };
        Grid.SetColumn(bytesLabel, 2);
        headerRow.Children.Add(bytesLabel);

        panel.Children.Add(headerRow);

        panel.Children.Add(new ProgressBar
        {
            Value = hf.PeakPercent,
            Maximum = 100,
            Height = Gap4,
            Margin = new Thickness(0, Gap4, 0, 0)
        });

        if (hf.CallChain.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Called from:",
                FontSize = FontSmall,
                Foreground = new SolidColorBrush(MediaColors.DimGray),
                Margin = new Thickness(0, Gap8, 0, Gap4)
            });
            foreach (var site in hf.CallChain)
                AddCallSiteRow(panel, site, 0);
        }

        card.Child = panel;
        return card;
    }

    private static void AddCallSiteRow(StackPanel panel, CallSite site, int depth)
    {
        var prefix = depth == 0 ? "• " : new string(' ', depth * 3) + "└ ";
        panel.Children.Add(new TextBlock
        {
            Text = $"{prefix}{site.Label}  —  {ByteFormatter.Format(site.Bytes)}",
            FontFamily = new FontFamily("Cascadia Code,Consolas,monospace"),
            FontSize = FontSmall,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(Gap8, 1, 0, 1)
        });
        foreach (var caller in site.Callers)
            AddCallSiteRow(panel, caller, depth + 1);
    }

    private void ShowSnapshot(MassifSnapshot snap)
    {
        TreeList.Items.Clear();
        if (snap.TreeType != TreeType.Empty && snap.HeapTree.Count > 0)
        {
            TreeHeader.Text = $"Snapshot #{snap.Index} — {snap.TreeType} — Total heap: {ByteFormatter.Format(snap.MemHeapB)}";
            var flat = new List<HeapNode>();
            Flatten(snap.HeapTree, flat);
            foreach (var n in flat)
                TreeList.Items.Add(n);
        }
        else
        {
            TreeHeader.Text = $"Snapshot #{snap.Index} — regular sample (no allocation tree recorded).";
        }

        DetailsPanel.Children.Clear();
        AddDetail("Snapshot #",     snap.Index.ToString());
        AddDetail("Time",           snap.Time.ToString("N0"));
        AddDetail("Heap",           ByteFormatter.Format(snap.MemHeapB));
        AddDetail("Admin overhead", ByteFormatter.Format(snap.MemHeapExtraB));
        AddDetail("Stack",          ByteFormatter.Format(snap.MemStacksB));
        AddDetail("Total",          ByteFormatter.Format(snap.TotalB));
        AddDetail("Tree type",      snap.TreeType.ToString());
        if (_profile != null && _profile.MaxHeap > 0)
            AddDetail("% of peak", $"{snap.MemHeapB * 100.0 / _profile.MaxHeap:F1}%");

        StatusBar.Text = $"Snapshot #{snap.Index} — heap: {ByteFormatter.Format(snap.MemHeapB)} — time: {snap.Time:N0}";
    }

    private void AddDetail(string label, string value)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions($"{LabelColumnWidth},*") };
        row.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Avalonia.Media.Brushes.DimGray,
            FontSize = FontNormal,
            [Grid.ColumnProperty] = 0
        });
        row.Children.Add(new TextBlock
        {
            Text = value,
            Foreground = Avalonia.Media.Brushes.Black,
            FontSize = FontNormal,
            FontWeight = Avalonia.Media.FontWeight.Medium,
            [Grid.ColumnProperty] = 1
        });
        DetailsPanel.Children.Add(row);
    }

    private static void Flatten(List<HeapNode> nodes, List<HeapNode> result)
    {
        foreach (var n in nodes) { result.Add(n); Flatten(n.Children, result); }
    }

    private static string BuildSummary(MassifProfile p)
    {
        var peak = p.PeakSnapshot;
        double peakMb = peak != null ? peak.MemHeapB / 1_048_576.0 : 0;
        return $"Command: {p.Command}   |   Time unit: {p.TimeUnit}   |   " +
               $"Snapshots: {p.Snapshots.Count}   |   Peak heap: {peakMb:F2} MB";
    }
}
