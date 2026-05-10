using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using MediaColors = Avalonia.Media.Colors;
using Avalonia.Platform.Storage;
using MassifVisualizer.Models;
using MassifVisualizer.Services;
using ScottPlot;

namespace MassifVisualizer;

public partial class MainWindow : Window
{
    private MassifProfile? _profile;

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

        int w = Math.Max(400, (int)ChartBorder.Bounds.Width);
        int h = Math.Max(200, (int)ChartBorder.Bounds.Height);

        var pngBytes = BuildPlot(_profile, w, h);
        using var ms = new MemoryStream(pngBytes);
        ChartImage.Source = new Bitmap(ms);
    }

    // Converts an Avalonia named color to a ScottPlot color so we can use
    // named colors (e.g. MediaColors.LightSkyBlue) instead of raw hex strings.
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
        extraLine.LineWidth  = 1.5f;
        extraLine.LegendText = "Heap + Admin";

        var heapLine = plot.Add.SignalXY(times, heap);
        heapLine.Color      = SP(MediaColors.SteelBlue);
        heapLine.LineWidth  = 2.5f;
        heapLine.LegendText = "Heap";

        foreach (var s in snaps.Where(s => s.TreeType != TreeType.Empty))
        {
            bool isPeak = s.TreeType == TreeType.Peak;
            var marker = plot.Add.Marker(s.Time, s.MemHeapB);
            marker.Color  = isPeak ? SP(MediaColors.Crimson) : SP(MediaColors.ForestGreen);
            marker.Shape  = isPeak ? MarkerShape.FilledDiamond : MarkerShape.FilledCircle;
            marker.Size   = isPeak ? 12 : 8;
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
        AddDetail("Snapshot #",    snap.Index.ToString());
        AddDetail("Time",          snap.Time.ToString("N0"));
        AddDetail("Heap",          ByteFormatter.Format(snap.MemHeapB));
        AddDetail("Admin overhead", ByteFormatter.Format(snap.MemHeapExtraB));
        AddDetail("Stack",         ByteFormatter.Format(snap.MemStacksB));
        AddDetail("Total",         ByteFormatter.Format(snap.TotalB));
        AddDetail("Tree type",     snap.TreeType.ToString());
        if (_profile != null && _profile.MaxHeap > 0)
            AddDetail("% of peak", $"{snap.MemHeapB * 100.0 / _profile.MaxHeap:F1}%");

        StatusBar.Text = $"Snapshot #{snap.Index} — heap: {ByteFormatter.Format(snap.MemHeapB)} — time: {snap.Time:N0}";
    }

    private void AddDetail(string label, string value)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("140,*") };
        row.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Avalonia.Media.Brushes.DimGray,
            FontSize = 12,
            [Grid.ColumnProperty] = 0
        });
        row.Children.Add(new TextBlock
        {
            Text = value,
            Foreground = Avalonia.Media.Brushes.Black,
            FontSize = 12,
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
