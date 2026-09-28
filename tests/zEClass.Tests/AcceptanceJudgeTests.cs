using System;
using System.Collections.Generic;
using System.Linq;
using zEClass.Core;
using Xunit;

namespace zEClass.Tests;

/// <summary>
/// Tests for the acceptance judging rules.
///
/// The point of these is not that pass/fail arithmetic works, it is that a missing capability
/// reports SKIP and never PASS. A report that says a pen-only panel "passed" pen checks would
/// hide exactly the hardware problem the run exists to find.
/// </summary>
public sealed class AcceptanceJudgeTests
{
    private static readonly AcceptanceThresholds Limits = new();

    private static DigitizerStatus Status(
        bool touch = false, bool pen = false, int devices = 1) => new(
        touch,
        pen,
        devices > 0,
        "test panel",
        Enumerable.Range(0, devices)
            .Select(i => new DigitizerDeviceInfo(
                $"Test Digitizer {i}", $@"\\?\hid#test{i}", DigitizerKind.TouchAndPen, 0x1234, 0x5678,
                true, true, "Test"))
            .ToList());

    // ---------------------------------------------------------------- digitizer

    [Fact]
    public void Digitizer_WithNoDevices_SkipsRatherThanPasses()
    {
        var check = AcceptanceJudge.DigitizerPresent(Status(devices: 0));

        Assert.Equal(CheckOutcome.Skipped, check.Outcome);
        Assert.Contains("USB touch cable", check.Detail);
    }

    [Fact]
    public void Digitizer_WithDevicesButNeitherActive_Fails()
    {
        var check = AcceptanceJudge.DigitizerPresent(Status(devices: 2));

        Assert.Equal(CheckOutcome.Fail, check.Outcome);
        Assert.Contains("touch switched off", check.Detail);
    }

    [Fact]
    public void Digitizer_WithPenActive_Passes()
    {
        var check = AcceptanceJudge.DigitizerPresent(Status(pen: true));

        Assert.Equal(CheckOutcome.Pass, check.Outcome);
    }

    // ---------------------------------------------------------------- touch accuracy

    [Fact]
    public void TouchAccuracy_WithNoHits_Skips()
    {
        var check = AcceptanceJudge.TouchAccuracy([], 100, 100, Limits);

        Assert.Equal(CheckOutcome.Skipped, check.Outcome);
    }

    [Fact]
    public void TouchAccuracy_WithinLimit_Passes()
    {
        var hits = new List<(double, double)> { (105, 100), (100, 95), (100, 100) };

        var check = AcceptanceJudge.TouchAccuracy(hits, 100, 100, Limits);

        Assert.Equal(CheckOutcome.Pass, check.Outcome);
    }

    [Fact]
    public void TouchAccuracy_JustOverLimit_FailsAndKeepsMeasurements()
    {
        var hits = new List<(double, double)> { (100, 100), (200, 100) };

        var check = AcceptanceJudge.TouchAccuracy(hits, 100, 100, Limits);

        Assert.Equal(CheckOutcome.Fail, check.Outcome);
        Assert.Equal(2, check.Measurements.Count);
        Assert.Equal(100, check.Measurements[1].Value, 3);
    }

    [Fact]
    public void TouchAccuracy_UsesEuclideanDistanceNotPerAxis()
    {
        // 30px on each axis is 42.4px away, which must fail a 40px limit.
        var hits = new List<(double, double)> { (130, 130) };

        var check = AcceptanceJudge.TouchAccuracy(hits, 100, 100, Limits);

        Assert.Equal(CheckOutcome.Fail, check.Outcome);
    }

    // ---------------------------------------------------------------- multi-touch

    [Fact]
    public void MultiTouch_SingleContact_SkipsWithAnExplanation()
    {
        var check = AcceptanceJudge.MultiTouch(1, Limits);

        Assert.Equal(CheckOutcome.Skipped, check.Outcome);
        Assert.Contains("gestures", check.Detail);
    }

    [Fact]
    public void MultiTouch_NoContacts_Fails()
    {
        var check = AcceptanceJudge.MultiTouch(0, Limits);

        Assert.Equal(CheckOutcome.Fail, check.Outcome);
    }

    [Fact]
    public void MultiTouch_TwoContacts_Passes()
    {
        var check = AcceptanceJudge.MultiTouch(2, Limits);

        Assert.Equal(CheckOutcome.Pass, check.Outcome);
        Assert.Equal(2, check.Measurements[0].Value);
    }

    [Fact]
    public void MultiTouch_FourContacts_Passes()
    {
        Assert.Equal(CheckOutcome.Pass, AcceptanceJudge.MultiTouch(4, Limits).Outcome);
    }

    // ---------------------------------------------------------------- pressure

    [Fact]
    public void Pressure_WithTooFewSamples_Skips()
    {
        var check = AcceptanceJudge.PressureResponse([0.5, 0.6], [0.4, 0.6], Limits);

        Assert.Equal(CheckOutcome.Skipped, check.Outcome);
    }

    [Fact]
    public void Pressure_ConstantPressure_SkipsAndPointsAtTheDriver()
    {
        // A pen that always reports 1023: raw span is zero, so this is a driver problem, not a
        // failing panel, and must not be reported as a failure of the hardware.
        var raw = Enumerable.Repeat(1023d, 40).ToList();
        var norm = Enumerable.Repeat(0.5d, 40).ToList();

        var check = AcceptanceJudge.PressureResponse(raw, norm, Limits);

        Assert.Equal(CheckOutcome.Skipped, check.Outcome);
        Assert.Contains("pen driver", check.Detail);
    }

    [Fact]
    public void Pressure_WideSpan_Passes()
    {
        var raw = Enumerable.Range(0, 40).Select(i => (double)(100 + (i * 20))).ToList();
        var norm = Enumerable.Range(0, 40).Select(i => i / 39d).ToList();

        var check = AcceptanceJudge.PressureResponse(raw, norm, Limits);

        Assert.Equal(CheckOutcome.Pass, check.Outcome);
        Assert.Contains("spanned", check.Detail);
    }

    [Fact]
    public void Pressure_NarrowSpan_Fails()
    {
        var raw = Enumerable.Range(0, 40).Select(i => (double)(500 + (i * 3))).ToList();
        var norm = Enumerable.Range(0, 40).Select(i => 0.50 + (i * 0.002)).ToList();

        var check = AcceptanceJudge.PressureResponse(raw, norm, Limits);

        Assert.Equal(CheckOutcome.Fail, check.Outcome);
        Assert.Contains("barely respond", check.Detail);
    }

    [Fact]
    public void Pressure_RecordsBothRawScales()
    {
        // Same physical stroke, one driver reporting 0..1 and another 0..1023. Both must pass,
        // which is the point of judging on the normalized span.
        var narrow = Enumerable.Range(0, 30).Select(i => 0.05 + (i * 0.03)).ToList();
        var narrowNorm = Enumerable.Range(0, 30).Select(i => 0.02 + (i * 0.033)).ToList();
        var wide = Enumerable.Range(0, 30).Select(i => 51d + (i * 30d)).ToList();

        var a = AcceptanceJudge.PressureResponse(narrow, narrowNorm, Limits);
        var b = AcceptanceJudge.PressureResponse(wide, narrowNorm, Limits);

        Assert.Equal(CheckOutcome.Pass, a.Outcome);
        Assert.Equal(CheckOutcome.Pass, b.Outcome);
    }

    // ---------------------------------------------------------------- eraser

    [Fact]
    public void Eraser_NoPenUsed_Skips()
    {
        var check = AcceptanceJudge.EraserTip(sawEraser: false, sawPen: false, penWasUsed: false);

        Assert.Equal(CheckOutcome.Skipped, check.Outcome);
    }

    [Fact]
    public void Eraser_PenUsedButNeverFlipped_Fails()
    {
        var check = AcceptanceJudge.EraserTip(sawEraser: false, sawPen: true, penWasUsed: true);

        Assert.Equal(CheckOutcome.Fail, check.Outcome);
        Assert.Contains("barrel switch", check.Detail);
    }

    [Fact]
    public void Eraser_FlippedAndPenSeen_Passes()
    {
        var check = AcceptanceJudge.EraserTip(sawEraser: true, sawPen: true, penWasUsed: true);

        Assert.Equal(CheckOutcome.Pass, check.Outcome);
        Assert.Contains("both", check.Detail);
    }

    // ---------------------------------------------------------------- palm rejection

    [Fact]
    public void PalmRejection_NoPen_Skips()
    {
        Assert.Equal(CheckOutcome.Skipped,
            AcceptanceJudge.PalmRejection(true, false, true).Outcome);
    }

    [Fact]
    public void PalmRejection_NoTouchWhilePenDown_Skips()
    {
        var check = AcceptanceJudge.PalmRejection(false, true, false);

        Assert.Equal(CheckOutcome.Skipped, check.Outcome);
        Assert.Contains("nothing to reject", check.Detail);
    }

    [Fact]
    public void PalmRejection_TouchAcceptedWhilePenDown_Fails()
    {
        var check = AcceptanceJudge.PalmRejection(true, true, true);

        Assert.Equal(CheckOutcome.Fail, check.Outcome);
        Assert.Contains("resting palm will draw", check.Detail);
    }

    [Fact]
    public void PalmRejection_TouchRejectedWhilePenDown_Passes()
    {
        var check = AcceptanceJudge.PalmRejection(false, true, true);

        Assert.Equal(CheckOutcome.Pass, check.Outcome);
    }

    // ---------------------------------------------------------------- calibration

    [Fact]
    public void Calibration_TooFewTargets_Skips()
    {
        var check = AcceptanceJudge.CalibrationAccuracy(
            CalibrationTransform.Identity, 5, 2, Limits);

        Assert.Equal(CheckOutcome.Skipped, check.Outcome);
    }

    [Fact]
    public void Calibration_UnsolvableTransform_Fails()
    {
        var degenerate = new CalibrationTransform(1, 0, 0, 0, 1, 0, 0, 0, 0);

        var check = AcceptanceJudge.CalibrationAccuracy(degenerate, 5, 4, Limits);

        Assert.Equal(CheckOutcome.Fail, check.Outcome);
        Assert.Contains("did not produce a solvable transform", check.Detail);
    }

    [Fact]
    public void Calibration_ValidButNoErrorRecorded_Skips()
    {
        var check = AcceptanceJudge.CalibrationAccuracy(
            CalibrationTransform.Identity, double.NaN, 4, Limits);

        Assert.Equal(CheckOutcome.Skipped, check.Outcome);
        Assert.Contains("accuracy is unknown", check.Detail);
    }

    [Fact]
    public void Calibration_ErrorOverTwelvePixels_Fails()
    {
        // 12 px is the documented limit; the boundary itself must pass.
        Assert.Equal(CheckOutcome.Pass,
            AcceptanceJudge.CalibrationAccuracy(CalibrationTransform.Identity, 12.0, 4, Limits).Outcome);
        Assert.Equal(CheckOutcome.Fail,
            AcceptanceJudge.CalibrationAccuracy(CalibrationTransform.Identity, 12.1, 4, Limits).Outcome);
    }

    // ---------------------------------------------------------------- latency

    [Fact]
    public void Latency_TooFewSamples_Skips()
    {
        Assert.Equal(CheckOutcome.Skipped, AcceptanceJudge.Latency([1, 2], Limits).Outcome);
    }

    [Fact]
    public void Latency_UsesPercentilesNotTheWorstSample()
    {
        // One 300 ms outlier in twenty samples must not fail the check: that is a GC pause or a
        // page switch, not a sustained latency problem.
        var samples = Enumerable.Repeat(8d, 19).Append(300d).ToList();

        var check = AcceptanceJudge.Latency(samples, Limits);

        Assert.Equal(CheckOutcome.Pass, check.Outcome);
        Assert.Equal(300, check.Measurements.Single(m => m.Key == "worst").Value);
    }

    [Fact]
    public void Latency_SustainedSlow_P95Fails()
    {
        var samples = Enumerable.Repeat(60d, 20).ToList();

        var check = AcceptanceJudge.Latency(samples, Limits);

        Assert.Equal(CheckOutcome.Fail, check.Outcome);
        Assert.Contains("visibly trail", check.Detail);
    }

    [Fact]
    public void Latency_DoesNotMutateTheCallersSequence()
    {
        var samples = new List<double> { 50, 10, 30, 20 };

        AcceptanceJudge.Latency(samples, Limits);

        Assert.Equal([50d, 10d, 30d, 20d], samples);
    }

    // ---------------------------------------------------------------- report

    [Fact]
    public void Run_WithSkips_IsNotComplete()
    {
        var run = new AcceptanceRun();
        run.Checks.Add(AcceptanceJudge.MultiTouch(1, Limits));
        run.Checks.Add(AcceptanceJudge.PressureResponse([1], [1], Limits));

        Assert.False(run.IsComplete);
        Assert.Contains("not a full pass", run.ToMarkdown());
    }

    [Fact]
    public void Run_WithAFailure_IsNotComplete()
    {
        var run = new AcceptanceRun();
        run.Checks.Add(AcceptanceJudge.MultiTouch(2, Limits));
        run.Checks.Add(AcceptanceJudge.MultiTouch(0, Limits));

        Assert.False(run.IsComplete);
        Assert.Equal(1, run.PassCount);
        Assert.Equal(1, run.FailCount);
    }

    [Fact]
    public void Run_AllPassing_IsComplete()
    {
        var run = new AcceptanceRun { PanelDescription = "Panel A" };
        run.Checks.Add(AcceptanceJudge.MultiTouch(2, Limits));
        run.Checks.Add(AcceptanceJudge.EraserTip(true, true, true));

        Assert.True(run.IsComplete);
        Assert.Contains("2 passing", run.ToMarkdown());
    }

    [Fact]
    public void Run_EmptyChecks_IsNotComplete()
    {
        Assert.False(new AcceptanceRun().IsComplete);
    }

    [Fact]
    public void Run_RoundTripsThroughJson()
    {
        var run = new AcceptanceRun
        {
            BoardId = "abc",
            PanelDescription = "Test Panel",
            InputPath = "wpf-fallback",
            ScreenDescription = "3840x2160",
            Thresholds = new AcceptanceThresholds { MaxLatencyMs = 25 },
            Checks = { AcceptanceJudge.MultiTouch(2, Limits) },
        };

        var restored = AcceptanceRun.FromJson(run.ToJson());

        Assert.Equal(run.BoardId, restored.BoardId);
        Assert.Equal(run.PanelDescription, restored.PanelDescription);
        Assert.Equal(run.InputPath, restored.InputPath);
        Assert.Equal(25, restored.Thresholds.MaxLatencyMs);
        Assert.Single(restored.Checks);
        Assert.Equal(CheckOutcome.Pass, restored.Checks[0].Outcome);
    }

    [Fact]
    public void Run_MarkdownEscapesPipesInDetail()
    {
        var run = new AcceptanceRun();
        var check = AcceptanceJudge.MultiTouch(0, Limits);
        check.Detail = "a | b";
        run.Checks.Add(check);

        // A raw pipe would break the table layout for whoever reads the report.
        Assert.Contains(@"a \| b", run.ToMarkdown());
    }
}
