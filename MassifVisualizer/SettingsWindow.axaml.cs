using System;
using Avalonia.Controls;
using MassifVisualizer.Services.Detection;
using MassifVisualizer.Services.Profiling;

namespace MassifVisualizer;

public sealed record SettingsResult(MassifOptions Massif, DetectionThresholds Detection);

public partial class SettingsWindow : Window
{
    private static readonly string[] TimeUnits = ["i", "ms", "B"];

    public SettingsWindow() : this(MassifOptions.Default, DetectionThresholds.Default)
    {
    }

    public SettingsWindow(MassifOptions massif, DetectionThresholds detection)
    {
        InitializeComponent();
        TimeUnit.ItemsSource = TimeUnits;
        Populate(massif, detection);

        ResetButton.Click += (_, _) => Populate(MassifOptions.Default, DetectionThresholds.Default);
        CancelButton.Click += (_, _) => Close(null);
        ApplyButton.Click += (_, _) => Close(new SettingsResult(ReadMassifOptions(), ReadDetectionThresholds()));
    }

    private void Populate(MassifOptions massif, DetectionThresholds detection)
    {
        TimeUnit.SelectedItem = massif.TimeUnit;
        DetailedFrequency.Value = massif.DetailedFrequency;
        MaximumSnapshots.Value = massif.MaximumSnapshots;
        SignificanceThreshold.Value = (decimal)massif.SignificanceThreshold;

        MinSnapshots.Value = detection.MinSnapshots;
        LeakMonotonicity.Value = (decimal)detection.LeakMonotonicity;
        LeakMinGrowthShare.Value = (decimal)detection.LeakMinGrowthShare;
        LeakFinalRetention.Value = (decimal)detection.LeakFinalRetention;
        AtExitMinFinalShare.Value = (decimal)detection.AtExitMinFinalShare;
        FragMedianRatio.Value = (decimal)detection.FragMedianRatio;
        SpikeRelativeJump.Value = (decimal)detection.SpikeRelativeJump;
        SpikeAbsoluteJump.Value = (decimal)detection.SpikeAbsoluteJump;
        SpikeRecoveryWindow.Value = (decimal)detection.SpikeRecoveryWindow;
        SpikeRecoveryFraction.Value = (decimal)detection.SpikeRecoveryFraction;
    }

    private MassifOptions ReadMassifOptions() => new(
        TimeUnit.SelectedItem as string ?? MassifOptions.Default.TimeUnit,
        Decimal.ToInt32(DetailedFrequency.Value ?? 1),
        Decimal.ToInt32(MaximumSnapshots.Value ?? 1),
        Decimal.ToDouble(SignificanceThreshold.Value ?? 0));

    private DetectionThresholds ReadDetectionThresholds() => new()
    {
        MinSnapshots = Decimal.ToInt32(MinSnapshots.Value ?? 1),
        LeakMonotonicity = Decimal.ToDouble(LeakMonotonicity.Value ?? 0),
        LeakMinGrowthShare = Decimal.ToDouble(LeakMinGrowthShare.Value ?? 0),
        LeakFinalRetention = Decimal.ToDouble(LeakFinalRetention.Value ?? 0),
        AtExitMinFinalShare = Decimal.ToDouble(AtExitMinFinalShare.Value ?? 0),
        FragMedianRatio = Decimal.ToDouble(FragMedianRatio.Value ?? 0),
        SpikeRelativeJump = Decimal.ToDouble(SpikeRelativeJump.Value ?? 0),
        SpikeAbsoluteJump = Decimal.ToDouble(SpikeAbsoluteJump.Value ?? 0),
        SpikeRecoveryWindow = Decimal.ToDouble(SpikeRecoveryWindow.Value ?? 0),
        SpikeRecoveryFraction = Decimal.ToDouble(SpikeRecoveryFraction.Value ?? 0)
    };
}
