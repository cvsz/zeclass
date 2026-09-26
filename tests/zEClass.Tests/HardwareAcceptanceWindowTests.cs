using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using zEClass.Core;
using Xunit;

namespace zEClass.Tests;

/// <summary>
/// Construction smoke tests for the acceptance window.
///
/// The judging rules are unit tested elsewhere; what is tested here is that the window can
/// actually be built and that its step list and live surface are wired up. A window that only
/// throws when a teacher opens it is exactly the kind of defect the acceptance run exists to
/// catch, so it would be poor form to ship one untested.
/// </summary>
[Collection("WpfWindows")]
public sealed class HardwareAcceptanceWindowTests
{
    private readonly StaUiFixture _ui;

    public HardwareAcceptanceWindowTests(StaUiFixture ui) => _ui = ui;

    private void RunSta(Action action)
    {
        try
        {
            _ui.Invoke(action);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("acceptance window failed to construct", ex);
        }
    }

    [Fact]
    public void Window_ConstructsWithoutThrowing()
    {
        RunSta(() =>
        {
            var window = new HardwareAcceptanceWindow();
            Assert.NotNull(window);
        });
    }

    [Fact]
    public void Window_ExposesACheckForEveryStep()
    {
        RunSta(() =>
        {
            var window = new HardwareAcceptanceWindow();
            var run = window.RunForTesting;

            // Every capability in the roadmap claim list must have a check, or the run would
            // report a clean sheet while proving nothing about it.
            var ids = run.Checks.Select(c => c.Title).ToList();
            Assert.Contains("Pen pressure", ids);
            Assert.Contains("Palm rejection", ids);
            Assert.Contains("Simultaneous contacts", ids);
            Assert.Contains("Calibration accuracy", ids);
            Assert.Equal(ids.Count, ids.Distinct().Count());
        });
    }

    [Fact]
    public void Window_StartsWithNoChecksDecided()
    {
        RunSta(() =>
        {
            var window = new HardwareAcceptanceWindow();
            var run = window.RunForTesting;

            // Nothing is pre-judged. A check that reported pass before anyone touched the panel
            // would make the whole report worthless.
            Assert.All(run.Checks, c => Assert.False(c.IsDecided));
            Assert.False(run.IsComplete);
        });
    }

    [Fact]
    public void Window_ReportSerializesEvenWithNoPanelAttached()
    {
        RunSta(() =>
        {
            var window = new HardwareAcceptanceWindow();
            var run = window.RunForTesting;

            // The report must be producible on a machine with no panel, which is the state this
            // was developed in; that is exactly when a school needs it to explain itself.
            var md = run.ToMarkdown();
            Assert.Contains("# zEClass hardware acceptance run", md);
            Assert.Contains("| Check | Outcome | Detail |", md);

            var json = run.ToJson();
            var restored = AcceptanceRun.FromJson(json);
            Assert.Equal(run.Checks.Count, restored.Checks.Count);
        });
    }
}
