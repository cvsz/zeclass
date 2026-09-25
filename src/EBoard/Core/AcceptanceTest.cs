using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EBoard.Core;

/// <summary>Result of one acceptance check. <see cref="Skipped"/> is a first-class outcome.</summary>
public enum CheckOutcome
{
    Pending,
    Pass,
    Fail,

    /// <summary>
    /// The hardware needed to run the check is not present, so the check proves nothing.
    /// Deliberately not a pass: a report that calls a missing pen "pass" is worse than no report.
    /// </summary>
    Skipped,
}

/// <summary>A measured value recorded against a check, kept so the report is auditable.</summary>
public sealed class AcceptanceMeasurement
{
    public string Key { get; set; } = string.Empty;
    public double Value { get; set; }
    public string? Unit { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// One acceptance check: what to do, what was measured, and whether it passed.
/// </summary>
public sealed class AcceptanceCheck
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Instruction { get; set; } = string.Empty;
    public CheckOutcome Outcome { get; set; } = CheckOutcome.Pending;
    public string? Detail { get; set; }
    public List<AcceptanceMeasurement> Measurements { get; set; } = new();

    [JsonIgnore]
    public bool IsDecided => Outcome != CheckOutcome.Pending;

    public AcceptanceMeasurement Measure(string key, double value, string? unit = null,
        string? note = null)
    {
        var m = new AcceptanceMeasurement { Key = key, Value = value, Unit = unit, Note = note };
        Measurements.Add(m);
        return m;
    }

    public void Pass(string detail) => Decide(CheckOutcome.Pass, detail);

    public void Fail(string detail) => Decide(CheckOutcome.Fail, detail);

    public void Skip(string reason) => Decide(CheckOutcome.Skipped, reason);

    private void Decide(CheckOutcome outcome, string detail)
    {
        Outcome = outcome;
        Detail = detail;
    }
}

/// <summary>
/// Tunable limits for the acceptance run. Defaults are deliberately loose enough to pass on a
/// mediocre panel and tight enough to catch a genuinely broken one; a school that has a good
/// panel can tighten them and record why.
/// </summary>
public sealed class AcceptanceThresholds
{
    /// <summary>Worst-case calibration error in pixels. Matches the 12 px rule in HARDWARE.md.</summary>
    public double MaxCalibrationErrorPx { get; set; } = 12.0;

    /// <summary>How far a touch may land from an on-screen target, in pixels.</summary>
    public double MaxTouchErrorPx { get; set; } = 40.0;

    /// <summary>Minimum normalized pressure span over a stroke, to prove pressure is real.</summary>
    public double MinPressureSpan { get; set; } = 0.25;

    /// <summary>Minimum number of simultaneous contacts needed to pass the multi-touch check.</summary>
    public int MinSimultaneousContacts { get; set; } = 2;

    /// <summary>Worst-case pointer-to-render budget in milliseconds.</summary>
    public double MaxLatencyMs { get; set; } = 40.0;

    /// <summary>Pressure is treated as absent when the span is below this, for reporting only.</summary>
    public double PressureAbsentSpan { get; set; } = 0.02;
}

/// <summary>
/// A complete acceptance run against one panel, serializable so it can be attached to a support
/// ticket or a procurement record.
/// </summary>
public sealed class AcceptanceRun
{
    public string BoardId { get; set; } = Guid.Empty.ToString();
    public string PanelDescription { get; set; } = string.Empty;
    public string InputPath { get; set; } = string.Empty;
    public string ScreenDescription { get; set; } = string.Empty;
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset CompletedUtc { get; set; }
    public AcceptanceThresholds Thresholds { get; set; } = new();
    public List<AcceptanceCheck> Checks { get; set; } = new();

    [JsonIgnore]
    public int PassCount => Checks.Count(c => c.Outcome == CheckOutcome.Pass);

    [JsonIgnore]
    public int FailCount => Checks.Count(c => c.Outcome == CheckOutcome.Fail);

    [JsonIgnore]
    public int SkipCount => Checks.Count(c => c.Outcome == CheckOutcome.Skipped);

    [JsonIgnore]
    public int PendingCount => Checks.Count(c => c.Outcome == CheckOutcome.Pending);

    /// <summary>
    /// True only when every check passed. A run containing a skip is explicitly not a pass, because
    /// a skipped check means a whole capability went unverified and a green light here would hide
    /// exactly the gap this run was commissioned to find.
    /// </summary>
    [JsonIgnore]
    public bool IsComplete => Checks.Count > 0 && FailCount == 0 && SkipCount == 0 && PendingCount == 0;

    public static string Describe(AcceptanceRun run) => run.ToMarkdown();

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    });

    public static AcceptanceRun FromJson(string json) =>
        JsonSerializer.Deserialize<AcceptanceRun>(json)
        ?? throw new FormatException("not an acceptance run");

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# EBoard hardware acceptance run");
        sb.AppendLine();
        sb.AppendLine($"- Board: `{BoardId}`");
        sb.AppendLine($"- Panel: {PanelDescription}");
        sb.AppendLine($"- Display: {ScreenDescription}");
        sb.AppendLine($"- Input path: {InputPath}");
        sb.AppendLine($"- Started: {StartedUtc:u}");
        sb.AppendLine($"- Completed: {CompletedUtc:u}");
        sb.AppendLine($"- Result: **{Summary()}**");
        sb.AppendLine();
        sb.AppendLine("| Check | Outcome | Detail |");
        sb.AppendLine("| --- | --- | --- |");
        foreach (var c in Checks)
        {
            var detail = (c.Detail ?? string.Empty).Replace("|", "\\|");
            sb.AppendLine($"| {c.Title} | {Label(c.Outcome)} | {detail} |");
        }

        var measured = Checks.Where(c => c.Measurements.Count > 0).ToList();
        if (measured.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Measurements");
            foreach (var c in measured)
            {
                sb.AppendLine();
                sb.AppendLine($"### {c.Title}");
                foreach (var m in c.Measurements)
                {
                    var unit = string.IsNullOrEmpty(m.Unit) ? string.Empty : " " + m.Unit;
                    var note = string.IsNullOrEmpty(m.Note) ? string.Empty : $" ({m.Note})";
                    sb.AppendLine($"- {m.Key}: {m.Value.ToString("0.###", CultureInfo.InvariantCulture)}{unit}{note}");
                }
            }
        }

        return sb.ToString();
    }

    private static string Label(CheckOutcome o) => o switch
    {
        CheckOutcome.Pass => "PASS",
        CheckOutcome.Fail => "FAIL",
        CheckOutcome.Skipped => "SKIP",
        _ => "not run",
    };

    private string Summary() => FailCount > 0
        ? $"{FailCount} failing, {PassCount} passing, {SkipCount} skipped"
        : SkipCount > 0
            ? $"{PassCount} passing, {SkipCount} skipped (not a full pass)"
            : $"{PassCount} passing";
}

/// <summary>
/// The judging logic, kept free of any WPF or Win32 dependency so every rule can be unit tested
/// on a machine with no panel attached. Capturing the measurements is the harness's job; deciding
/// whether they are acceptable lives here.
/// </summary>
public static class AcceptanceJudge
{
    public static AcceptanceCheck DigitizerPresent(DigitizerStatus status)
    {
        var check = new AcceptanceCheck
        {
            Id = "digitizer",
            Title = "Digitizer enumerated",
            Instruction = "No action needed.",
        };

        check.Measure("device count", status.Devices.Count, note: status.Summary);

        if (status.Devices.Count == 0)
        {
            check.Skip("no HID touch or pen device was found. Check the USB touch cable and driver.");
            return check;
        }

        foreach (var d in status.Devices)
        {
            check.Measure(d.DeviceName, d.VendorId, note: d.Summary);
        }

        if (status.Ready)
        {
            check.Pass($"digitizer active ({status.Summary})");
        }
        else
        {
            check.Fail(
                "devices were enumerated but neither touch nor pen is active. Windows may have " +
                "touch switched off: see docs\\HARDWARE.md section 3.");
        }

        return check;
    }

    public static AcceptanceCheck TouchAccuracy(
        IReadOnlyList<(double X, double Y)> hits, double targetX, double targetY,
        AcceptanceThresholds thresholds)
    {
        var check = new AcceptanceCheck
        {
            Id = "touch-accuracy",
            Title = "Touch lands where it is aimed",
            Instruction = "Touch the centre of the cross, four times, once per corner of the screen.",
        };

        if (hits.Count == 0)
        {
            check.Skip("no touch samples were observed. The panel may be pen-only on this test.");
            return check;
        }

        var worst = 0.0;
        foreach (var (x, y) in hits)
        {
            var err = Math.Sqrt(Math.Pow(x - targetX, 2) + Math.Pow(y - targetY, 2));
            check.Measure("hit error", err, "px");
            worst = Math.Max(worst, err);
        }

        if (worst <= thresholds.MaxTouchErrorPx)
        {
            check.Pass($"worst hit was {worst:F1} px from the target");
        }
        else
        {
            check.Fail(
                $"worst hit was {worst:F1} px from the target, over the {thresholds.MaxTouchErrorPx:F0} px " +
                "limit. Recalibrate with Align before judging the panel.");
        }

        return check;
    }

    public static AcceptanceCheck MultiTouch(int maxSimultaneousContacts, AcceptanceThresholds thresholds)
    {
        var check = new AcceptanceCheck
        {
            Id = "multitouch",
            Title = "Simultaneous contacts",
            Instruction = "Rest two or more fingers on the board and hold still.",
        };

        check.Measure("max simultaneous contacts", maxSimultaneousContacts);

        if (maxSimultaneousContacts >= thresholds.MinSimultaneousContacts)
        {
            check.Pass($"saw {maxSimultaneousContacts} contacts at once");
        }
        else if (maxSimultaneousContacts == 1)
        {
            check.Skip(
                "only one contact at a time. This is expected on a single-touch panel, but it also " +
                "means gestures and two-finger zoom cannot work on it.");
        }
        else
        {
            check.Fail(
                $"no contacts at all were observed, so the panel is not delivering touch over the " +
                "expected path");
        }

        return check;
    }

    public static AcceptanceCheck PressureResponse(
        IReadOnlyList<double> rawPressures, IReadOnlyList<double> normalizedPressures,
        AcceptanceThresholds thresholds)
    {
        var check = new AcceptanceCheck
        {
            Id = "pressure",
            Title = "Pen pressure",
            Instruction = "Draw one slow line, starting as light as you can and pressing harder.",
        };

        if (rawPressures.Count < 8)
        {
            check.Skip(
                "not enough pen samples. Either the pen has no pressure sensor or it was not used " +
                "for this check.");
            return check;
        }

        var rawMin = rawPressures.Min();
        var rawMax = rawPressures.Max();
        var rawSpan = rawMax - rawMin;
        check.Measure("raw pressure min", rawMin);
        check.Measure("raw pressure max", rawMax);
        check.Measure("raw span", rawSpan, note: DescribeRawScale(rawMax));

        var normMin = normalizedPressures.Min();
        var normMax = normalizedPressures.Max();
        var normSpan = normMax - normMin;
        check.Measure("normalized min", normMin);
        check.Measure("normalized max", normMax);
        check.Measure("normalized span", normSpan);

        if (rawSpan < thresholds.PressureAbsentSpan && normSpan < thresholds.MinPressureSpan)
        {
            check.Skip(
                $"pressure is constant at {rawMax.ToString("0.###", CultureInfo.InvariantCulture)}. " +
                "The board will draw uniform-width lines. Install the pen driver if the pen is " +
                "supposed to report pressure; see docs\\HARDWARE.md section 4.");
            return check;
        }

        if (normSpan >= thresholds.MinPressureSpan)
        {
            check.Pass($"normalized pressure spanned {normSpan:F2} across the stroke");
        }
        else
        {
            check.Fail(
                $"normalized pressure spanned only {normSpan:F2}, under the " +
                $"{thresholds.MinPressureSpan:F2} limit. Line width will barely respond.");
        }

        return check;
    }

    private static string DescribeRawScale(double max) =>
        max > 1.5 ? "driver reports a 0..1023 style scale" : "driver reports a 0..1 scale";

    public static AcceptanceCheck EraserTip(bool sawEraser, bool sawPen, bool penWasUsed)
    {
        var check = new AcceptanceCheck
        {
            Id = "eraser-tip",
            Title = "Barrel-switch eraser",
            Instruction = "Turn the pen over and write with the eraser end.",
        };

        if (!penWasUsed)
        {
            check.Skip("no pen contact was observed, so the eraser end could not be tested.");
            return check;
        }

        check.Measure("eraser end seen", sawEraser ? 1 : 0);

        if (sawEraser)
        {
            check.Pass(sawPen ? "pen and eraser ends both reported correctly" : "eraser end reported");
        }
        else
        {
            check.Fail(
                "the eraser end did not flip the pen into eraser mode. Either the barrel switch is " +
                "not reported by this driver, or the pen has no switch.");
        }

        return check;
    }

    public static AcceptanceCheck PalmRejection(bool sawTouchWhilePenDown, bool sawPen, bool sawTouch)
    {
        var check = new AcceptanceCheck
        {
            Id = "palm-rejection",
            Title = "Palm rejection",
            Instruction = "Rest your palm flat on the board and write with the pen at the same time.",
        };

        if (!sawPen)
        {
            check.Skip("no pen contact was observed, so palm rejection could not be tested.");
            return check;
        }

        if (!sawTouch)
        {
            check.Skip(
                "no touch contact was observed while the pen was down, so there was nothing to " +
                "reject. That may be a good panel, or the touch layer may not be working.");
            return check;
        }

        check.Measure("touch samples accepted while pen down", sawTouchWhilePenDown ? 1 : 0);

        if (sawTouchWhilePenDown)
        {
            check.Fail(
                "touch was accepted while the pen was in contact, so a resting palm will draw. " +
                "Confirm the touch and pen are coming through the same digitizer.");
        }
        else
        {
            check.Pass("every touch sample was rejected while the pen was down");
        }

        return check;
    }

    public static AcceptanceCheck CalibrationAccuracy(
        CalibrationTransform? transform, double reportedErrorPx, int captureCount,
        AcceptanceThresholds thresholds)
    {
        var check = new AcceptanceCheck
        {
            Id = "calibration",
            Title = "Calibration accuracy",
            Instruction = "Click Align, touch the four crosses, and accept.",
        };

        if (captureCount < Calibration.TargetCount)
        {
            check.Skip(
                $"only {captureCount} of {Calibration.TargetCount} targets were captured, so there " +
                "is no transform to judge.");
            return check;
        }

        if (transform is null || !transform.IsInvertible)
        {
            check.Fail(
                "the four hits did not produce a solvable transform. They were too close together " +
                "or fell on a line; redo the calibration hitting each cross centre.");
            return check;
        }

        check.Measure("worst-case error", reportedErrorPx, "px");
        check.Measure("limit", thresholds.MaxCalibrationErrorPx, "px");

        if (double.IsNaN(reportedErrorPx))
        {
            check.Skip("calibration is valid but no error figure was recorded, so accuracy is unknown.");
            return check;
        }

        if (reportedErrorPx <= thresholds.MaxCalibrationErrorPx)
        {
            check.Pass($"worst-case error {reportedErrorPx:F1} px");
        }
        else
        {
            check.Fail(
                $"worst-case error {reportedErrorPx:F1} px is over the " +
                $"{thresholds.MaxCalibrationErrorPx:F0} px limit. Ink will be consistently offset.");
        }

        return check;
    }

    public static AcceptanceCheck Latency(IReadOnlyList<double> samplesMs, AcceptanceThresholds thresholds)
    {
        var check = new AcceptanceCheck
        {
            Id = "latency",
            Title = "Pointer to render latency",
            Instruction = "Draw a long slow spiral across the whole board.",
        };

        if (samplesMs.Count < 5)
        {
            check.Skip("not enough samples to measure latency.");
            return check;
        }

        // Sorted into a local list: the caller keeps ownership of the sequence, and a
        // Percentile over unsorted input would silently return nonsense.
        var ordered = samplesMs.ToList();
        ordered.Sort();
        var p50 = Percentile(ordered, 0.50);
        var p95 = Percentile(ordered, 0.95);
        var worst = ordered[^1];
        check.Measure("median", p50, "ms");
        check.Measure("p95", p95, "ms");
        check.Measure("worst", worst, "ms");

        if (p95 <= thresholds.MaxLatencyMs)
        {
            check.Pass($"p95 latency {p95:F1} ms");
        }
        else
        {
            check.Fail(
                $"p95 latency {p95:F1} ms is over the {thresholds.MaxLatencyMs:F0} ms budget. Ink " +
                "will visibly trail the pen on a large page.");
        }

        return check;
    }

    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0)
        {
            return 0;
        }

        var idx = (int)Math.Round((sorted.Count - 1) * p, MidpointRounding.AwayFromZero);
        return sorted[Math.Clamp(idx, 0, sorted.Count - 1)];
    }
}
