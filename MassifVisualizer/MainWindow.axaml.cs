using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
using MassifVisualizer.Services.Detection;
using MassifVisualizer.Services.Profiling;
using ScottPlot;

namespace MassifVisualizer;

public partial class MainWindow : Window
{
    private MassifProfile? _profile;
    private List<HotFunction> _allHotFunctions = [];
    private List<Finding> _findings = [];
    private int _topN = 5;   // matches IsChecked="True" on TopN5 in the XAML
    private CancellationTokenSource? _profiling;

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

    private const double SeverityBarWidth   = 4;
    private const double FindingCardSpacing = 6;
    private const double FindingBadgeWidth  = 70;

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
        // Avalonia checks the new button before unchecking the old one, so for a moment two of
        // them report IsChecked. That is why the count comes from the handler, not the buttons.
        TopN3.IsCheckedChanged  += (_, _) => { if (TopN3.IsChecked  == true) ShowTopN(3); };
        TopN5.IsCheckedChanged  += (_, _) => { if (TopN5.IsChecked  == true) ShowTopN(5); };
        TopN10.IsCheckedChanged += (_, _) => { if (TopN10.IsChecked == true) ShowTopN(10); };
        FilterCritical.IsCheckedChanged += (_, _) => RenderFindings();
        FilterWarning.IsCheckedChanged  += (_, _) => RenderFindings();
        FilterInfo.IsCheckedChanged     += (_, _) => RenderFindings();
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

        MenuOpenSource.Click += async (_, _) =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open C Source File",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("C source") { Patterns = new[] { "*.c" } },
                    FilePickerFileTypes.All
                }
            });

            if (files is { Count: > 0 })
                await ProfileSource(files[0].Path.LocalPath);
        };

        MenuExit.Click += (_, _) => Close();

        CancelButton.Click += (_, _) =>
        {
            _profiling?.Cancel();
            StatusBar.Text = "Cancelling...";
        };

        ToolOutputClose.Click += (_, _) => ToolOutputPanel.IsVisible = false;
    }

    private void LoadFile(string path)
    {
        try
        {
            StatusBar.Text = $"Loading {path}...";
            ShowProfile(MassifParser.Parse(path), Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            StatusBar.Text = $"Error: {ex.Message}";
        }
    }

    private async Task ProfileSource(string path)
    {
        var name = Path.GetFileName(path);

        _profiling = new CancellationTokenSource();
        CancelButton.IsVisible = true;
        MenuOpen.IsEnabled = MenuOpenSource.IsEnabled = false;
        ToolOutputPanel.IsVisible = false;
        StatusBar.Text = $"Compiling and profiling {name}, this takes a while...";

        try
        {
            var result = await SourceProfiler.RunAsync(path, _profiling.Token);

            if (result.Profile == null)
            {
                StatusBar.Text = $"Could not profile {name}";
                ShowToolOutput($"Could not profile {name}", result.Output);
            }
            else
            {
                ShowProfile(result.Profile, name);

                // Warnings and a non-zero exit are worth seeing even though the run produced a profile
                if (result.ExitCode is int code && code != 0)
                    ShowToolOutput($"{name} exited with code {code}", result.Output);
                else if (result.Output.Length > 0)
                    ShowToolOutput($"Compiler warnings for {name}", result.Output);
            }
        }
        catch (OperationCanceledException)
        {
            StatusBar.Text = $"Cancelled profiling {name}";
        }
        catch (Exception ex)
        {
            StatusBar.Text = $"Error: {ex.Message}";
            ShowToolOutput($"Could not profile {name}", ex.Message);
        }
        finally
        {
            CancelButton.IsVisible = false;
            MenuOpen.IsEnabled = MenuOpenSource.IsEnabled = true;
            _profiling.Dispose();
            _profiling = null;
        }
    }

    private void ShowToolOutput(string header, string output)
    {
        ToolOutputHeader.Text = header;
        ToolOutputText.Text = output.Length > 0 ? output : "(no output)";
        ToolOutputPanel.IsVisible = true;
    }

    private void ShowProfile(MassifProfile profile, string name)
    {
        _profile = profile;

        SnapshotList.Items.Clear();
        foreach (var s in profile.Snapshots)
            SnapshotList.Items.Add(s);

        SummaryText.Text = BuildSummary(profile);
        RefreshHotFunctions();
        RefreshFindings();

        var peak = profile.PeakSnapshot ?? profile.Snapshots.LastOrDefault();
        if (peak != null)
            SnapshotList.SelectedItem = peak;
        else
            ShowNoSnapshot($"{name} has no snapshots to show.");

        StatusBar.Text = $"Loaded {profile.Snapshots.Count} snapshots from {name}";
    }

    // The tree and details tabs are driven by the selected snapshot, so they need something to say
    // when there is no selection. Before anything is loaded that text comes from the XAML
    private void ShowNoSnapshot(string reason)
    {
        TreeList.Items.Clear();
        TreeHeader.Text = reason;

        DetailsPanel.Children.Clear();
        DetailsPanel.Children.Add(new TextBlock
        {
            Text = reason,
            Foreground = Avalonia.Media.Brushes.DimGray,
            FontSize = FontNormal
        });
    }

    private void RefreshChart()
    {
        if (_profile == null || _profile.Snapshots.Count == 0) return;

        int w = Math.Max(ChartMinWidth,  (int)ChartBorder.Bounds.Width);
        int h = Math.Max(ChartMinHeight, (int)ChartBorder.Bounds.Height);

        var pngBytes = BuildPlot(_profile, w, h, _findings);
        using var ms = new MemoryStream(pngBytes);
        ChartImage.Source = new Bitmap(ms);
    }

    private static ScottPlot.Color SP(Avalonia.Media.Color c) => new(c.R, c.G, c.B, c.A);

    private static ScottPlot.Color SeverityColor(Severity s) => s switch
    {
        Severity.Critical => SP(MediaColors.Crimson),
        Severity.Warning  => SP(MediaColors.DarkOrange),
        _                 => SP(MediaColors.SteelBlue)
    };

    private static byte[] BuildPlot(MassifProfile profile, int width, int height, List<Finding>? findings = null)
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

        long TimeAt(double t)
        {
            if (snaps.Count == 0) return 0;
            if (snaps.Count == 1) return snaps[0].Time;
            long t0 = snaps[0].Time, tn = snaps[^1].Time;
            return t0 + (long)(t * (tn - t0));
        }

        foreach (var f in findings ?? [])
        {
            var span = plot.Add.HorizontalSpan(TimeAt(f.RangeStartT), TimeAt(f.RangeEndT));
            span.FillStyle.Color = SeverityColor(f.Severity).WithAlpha(40);
            span.LineStyle.Width = 0;
        }

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

    private void ShowTopN(int n)
    {
        _topN = n;
        RenderHotFunctions();
    }

    private void RenderHotFunctions()
    {
        HotFunctionsPanel.Children.Clear();

        var functions = _allHotFunctions.Take(_topN).ToList();

        if (functions.Count == 0)
        {
            HotFunctionsHeader.Text = "No detailed snapshot data available.";
            return;
        }

        HotFunctionsHeader.Text = $"{_allHotFunctions.Count} allocating function{(_allHotFunctions.Count == 1 ? "" : "s")}";

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
            Text = $"{hf.PeakDisplay}  ·  {hf.SharePercentDisplay}",
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
            Value = hf.SharePercent,
            Maximum = 100,
            Height = Gap4,
            Margin = new Thickness(0, Gap4, 0, 0)
        });

        var fate = hf.FinalBytes > 0
            ? $"{hf.FinalDisplay} still held in the last detailed snapshot"
            : "fully released by the last detailed snapshot";

        panel.Children.Add(new TextBlock
        {
            Text = $"Largest at snapshot #{hf.PeakSnapshotIndex}  ·  {fate}",
            FontSize = FontSmall,
            Foreground = new SolidColorBrush(MediaColors.DimGray),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, Gap4, 0, 0)
        });

        panel.Children.Add(new TextBlock
        {
            Text = hf.Sites.Count == 1
                ? $"Allocates at {hf.Sites[0].Location}"
                : $"Allocates at {hf.Sites.Count} lines in this function:",
            FontSize = FontSmall,
            Foreground = new SolidColorBrush(MediaColors.DimGray),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, Gap4, 0, 0)
        });

        if (hf.Sites.Count > 1)
            foreach (var site in hf.Sites)
                AddSiteRow(panel, site);

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

    private static void AddSiteRow(StackPanel panel, AllocationSite site)
    {
        panel.Children.Add(new TextBlock
        {
            Text = $"• {site.Location}  —  {site.BytesDisplay}",
            FontFamily = new FontFamily("Cascadia Code,Consolas,monospace"),
            FontSize = FontSmall,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(Gap8, 1, 0, 1)
        });
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

    private void RefreshFindings()
    {
        if (_profile == null) return;
        _findings = DetectionEngine.Run(_profile);
        RenderFindings();
        RefreshChart();
    }

    private void RenderFindings()
    {
        FindingsPanel.Children.Clear();

        if (_findings.Count == 0)
        {
            FindingsHeader.Text = "No problems detected.";
            return;
        }

        int critical = _findings.Count(f => f.Severity == Severity.Critical);
        int warning  = _findings.Count(f => f.Severity == Severity.Warning);
        int info     = _findings.Count(f => f.Severity == Severity.Info);
        var breakdown = new List<string>();
        if (critical > 0) breakdown.Add($"{critical} critical");
        if (warning  > 0) breakdown.Add($"{warning} warning");
        if (info     > 0) breakdown.Add($"{info} info");
        FindingsHeader.Text = $"{_findings.Count} finding{(_findings.Count == 1 ? "" : "s")} — {string.Join(", ", breakdown)}";

        var visible = _findings.Where(f => f.Severity switch
        {
            Severity.Critical => FilterCritical.IsChecked == true,
            Severity.Warning  => FilterWarning.IsChecked == true,
            _                 => FilterInfo.IsChecked == true
        });

        foreach (var f in visible)
            FindingsPanel.Children.Add(BuildFindingCard(f));
    }

    private static IBrush SeverityBrush(Severity s) => s switch
    {
        Severity.Critical => new SolidColorBrush(MediaColors.Crimson),
        Severity.Warning  => new SolidColorBrush(MediaColors.DarkOrange),
        _                 => new SolidColorBrush(MediaColors.SteelBlue)
    };

    private Control BuildFindingCard(Finding f)
    {
        var color = SeverityBrush(f.Severity);

        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(MediaColors.Silver),
            BorderThickness = new Thickness(1),
            CornerRadius = CardCorner,
            Padding = CardPadding
        };

        var outer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };

        var severityBar = new Border
        {
            Background = color,
            Width = SeverityBarWidth,
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 0, Gap8, 0)
        };
        Grid.SetColumn(severityBar, 0);
        outer.Children.Add(severityBar);

        var panel = new StackPanel { Spacing = 0 };

        var headerRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };

        var badge = new Border
        {
            Background = color,
            CornerRadius = BadgeCorner,
            Width = FindingBadgeWidth,
            Height = BadgeHeight,
            VerticalAlignment = VA.Center,
            Margin = new Thickness(0, 0, Gap8, 0),
            Child = new TextBlock
            {
                Text = f.Severity.ToString().ToUpperInvariant(),
                Foreground = Brushes.White,
                FontSize = FontTiny,
                FontWeight = FW.Bold,
                HorizontalAlignment = HA.Center,
                VerticalAlignment = VA.Center
            }
        };
        Grid.SetColumn(badge, 0);
        headerRow.Children.Add(badge);

        var titleLabel = new TextBlock
        {
            Text = f.Title,
            FontSize = FontNormal,
            FontWeight = FW.SemiBold,
            Foreground = Brushes.Black,
            VerticalAlignment = VA.Center,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(titleLabel, 1);
        headerRow.Children.Add(titleLabel);

        var confidenceLabel = new TextBlock
        {
            Text = $"Confidence: {f.ConfidenceDisplay}",
            FontSize = FontNormal,
            Foreground = new SolidColorBrush(MediaColors.DimGray),
            VerticalAlignment = VA.Center,
            Margin = new Thickness(Gap16, 0, 0, 0)
        };
        Grid.SetColumn(confidenceLabel, 2);
        headerRow.Children.Add(confidenceLabel);

        panel.Children.Add(headerRow);

        panel.Children.Add(new TextBlock
        {
            Text = f.Description,
            FontSize = FontSmall,
            Foreground = Brushes.Black,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, Gap4, 0, 0)
        });

        if (f.Evidence.Count > 0)
        {
            var evidencePanel = new StackPanel { Spacing = 1, Margin = new Thickness(0, Gap8, 0, 0) };
            foreach (var line in f.Evidence)
                evidencePanel.Children.Add(new TextBlock
                {
                    Text = line,
                    FontFamily = new FontFamily("Cascadia Code,Consolas,monospace"),
                    FontSize = FontSmall,
                    Foreground = new SolidColorBrush(MediaColors.DimGray)
                });
            panel.Children.Add(evidencePanel);
        }

        if (!string.IsNullOrEmpty(f.SuspectSite))
            panel.Children.Add(new TextBlock
            {
                Text = $"Suspect site: {f.SuspectSite}",
                FontSize = FontSmall,
                FontWeight = FW.SemiBold,
                Foreground = Brushes.Black,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, Gap8, 0, 0)
            });

        if (!string.IsNullOrEmpty(f.Suggestion))
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Suggested fix:",
                FontSize = FontSmall,
                Foreground = new SolidColorBrush(MediaColors.DimGray),
                Margin = new Thickness(0, Gap8, 0, Gap4)
            });
            panel.Children.Add(new TextBlock
            {
                Text = f.Suggestion,
                FontSize = FontSmall,
                Foreground = Brushes.Black,
                TextWrapping = TextWrapping.Wrap
            });
        }

        Grid.SetColumn(panel, 1);
        outer.Children.Add(panel);

        card.Child = outer;

        if (f.EvidenceSnapshotIndex is int idx)
            card.PointerPressed += (_, _) =>
            {
                var snap = _profile?.Snapshots.FirstOrDefault(s => s.Index == idx);
                if (snap != null) SnapshotList.SelectedItem = snap;
            };

        return card;
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
