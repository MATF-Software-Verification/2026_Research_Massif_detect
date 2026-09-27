using System;
using System.Globalization;
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

        ResetButton.Click += (_, _) =>
        {
            Populate(MassifOptions.Default, DetectionThresholds.Default);
            ClearValidationMessage();
        };
        CancelButton.Click += (_, _) => Close(null);
        ApplyButton.Click += (_, _) =>
        {
            if (TryReadSettings(out var settings))
                Close(settings);
        };
    }

    private void Populate(MassifOptions massif, DetectionThresholds detection)
    {
        TimeUnit.SelectedItem = massif.TimeUnit;
        DetailedFrequency.Text = massif.DetailedFrequency.ToString(CultureInfo.CurrentCulture);
        MaximumSnapshots.Text = massif.MaximumSnapshots.ToString(CultureInfo.CurrentCulture);
        SignificanceThreshold.Text = massif.SignificanceThreshold.ToString(CultureInfo.CurrentCulture);

        MinSnapshots.Text = detection.MinSnapshots.ToString(CultureInfo.CurrentCulture);
        LeakMonotonicity.Text = detection.LeakMonotonicity.ToString(CultureInfo.CurrentCulture);
        LeakMinGrowthShare.Text = detection.LeakMinGrowthShare.ToString(CultureInfo.CurrentCulture);
        LeakFinalRetention.Text = detection.LeakFinalRetention.ToString(CultureInfo.CurrentCulture);
        AtExitMinFinalShare.Text = detection.AtExitMinFinalShare.ToString(CultureInfo.CurrentCulture);
        FragMedianRatio.Text = detection.FragMedianRatio.ToString(CultureInfo.CurrentCulture);
        SpikeRelativeJump.Text = detection.SpikeRelativeJump.ToString(CultureInfo.CurrentCulture);
        SpikeAbsoluteJump.Text = detection.SpikeAbsoluteJump.ToString(CultureInfo.CurrentCulture);
        SpikeRecoveryWindow.Text = detection.SpikeRecoveryWindow.ToString(CultureInfo.CurrentCulture);
        SpikeRecoveryFraction.Text = detection.SpikeRecoveryFraction.ToString(CultureInfo.CurrentCulture);
    }

    private bool TryReadSettings(out SettingsResult? settings)
    {
        settings = null;
        ClearValidationMessage();

        if (!TryReadNumber(DetailedFrequency, "Detailed frequency", 1, int.MaxValue, integerOnly: true, out var detailedFrequency) ||
            !TryReadNumber(MaximumSnapshots, "Maximum snapshots", 1, int.MaxValue, integerOnly: true, out var maximumSnapshots) ||
            !TryReadNumber(SignificanceThreshold, "Significance threshold", 0, 100, integerOnly: false, out var significanceThreshold) ||
            !TryReadNumber(MinSnapshots, "Minimum snapshots", 1, int.MaxValue, integerOnly: true, out var minSnapshots) ||
            !TryReadNumber(LeakMonotonicity, "LEAK monotonicity", 0, 1, integerOnly: false, out var leakMonotonicity) ||
            !TryReadNumber(LeakMinGrowthShare, "LEAK minimum growth share", 0, 1, integerOnly: false, out var leakMinGrowthShare) ||
            !TryReadNumber(LeakFinalRetention, "LEAK final retention", 0, 1, integerOnly: false, out var leakFinalRetention) ||
            !TryReadNumber(AtExitMinFinalShare, "ATEXIT minimum final share", 0, 1, integerOnly: false, out var atExitMinFinalShare) ||
            !TryReadNumber(FragMedianRatio, "FRAG median overhead ratio", 0, 1, integerOnly: false, out var fragMedianRatio) ||
            !TryReadNumber(SpikeRelativeJump, "SPIKE relative jump", 0, 1, integerOnly: false, out var spikeRelativeJump) ||
            !TryReadNumber(SpikeAbsoluteJump, "SPIKE absolute jump", 0, 1, integerOnly: false, out var spikeAbsoluteJump) ||
            !TryReadNumber(SpikeRecoveryWindow, "SPIKE recovery window", 0, 1, integerOnly: false, out var spikeRecoveryWindow) ||
            !TryReadNumber(SpikeRecoveryFraction, "SPIKE recovery fraction", 0, 1, integerOnly: false, out var spikeRecoveryFraction))
            return false;

        var massif = new MassifOptions(
            TimeUnit.SelectedItem as string ?? MassifOptions.Default.TimeUnit,
            Decimal.ToInt32(detailedFrequency),
            Decimal.ToInt32(maximumSnapshots),
            Decimal.ToDouble(significanceThreshold));

        var detection = new DetectionThresholds
        {
            MinSnapshots = Decimal.ToInt32(minSnapshots),
            LeakMonotonicity = Decimal.ToDouble(leakMonotonicity),
            LeakMinGrowthShare = Decimal.ToDouble(leakMinGrowthShare),
            LeakFinalRetention = Decimal.ToDouble(leakFinalRetention),
            AtExitMinFinalShare = Decimal.ToDouble(atExitMinFinalShare),
            FragMedianRatio = Decimal.ToDouble(fragMedianRatio),
            SpikeRelativeJump = Decimal.ToDouble(spikeRelativeJump),
            SpikeAbsoluteJump = Decimal.ToDouble(spikeAbsoluteJump),
            SpikeRecoveryWindow = Decimal.ToDouble(spikeRecoveryWindow),
            SpikeRecoveryFraction = Decimal.ToDouble(spikeRecoveryFraction)
        };

        settings = new SettingsResult(massif, detection);
        return true;
    }

    private bool TryReadNumber(TextBox input, string label, decimal minimum, decimal maximum,
                               bool integerOnly, out decimal value)
    {
        value = default;
        var numberFormat = CultureInfo.CurrentCulture;
        if (string.IsNullOrWhiteSpace(input.Text) ||
            !decimal.TryParse(input.Text, NumberStyles.Float, numberFormat, out value))
        {
            ShowValidationMessage($"{label} must be a number.");
            return false;
        }

        if (value < minimum || value > maximum)
        {
            var range = $"between {minimum.ToString(numberFormat)} and {maximum.ToString(numberFormat)}";
            ShowValidationMessage($"{label} must be {range}.");
            return false;
        }

        if (integerOnly && decimal.Truncate(value) != value)
        {
            ShowValidationMessage($"{label} must be a whole number.");
            return false;
        }

        return true;
    }

    private void ShowValidationMessage(string message)
    {
        ValidationMessage.Text = message;
        ValidationMessage.IsVisible = true;
    }

    private void ClearValidationMessage()
    {
        ValidationMessage.Text = "";
        ValidationMessage.IsVisible = false;
    }
}
