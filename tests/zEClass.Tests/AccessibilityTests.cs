using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace zEClass.Tests;

/// <summary>
/// Machine-checkable slice of AGENTS §22 (accessibility). This file verifies the *static*
/// contract only: every interactive control in the main window exposes a name a screen
/// reader can use. It does not verify focus visibility, tab order, or what NVDA actually
/// speaks — those need a manual walkthrough and are recorded as NOT DONE until then.
/// </summary>
public sealed class AccessibilityTests
{
    private static readonly string MainWindowXaml = FindMainWindowXaml();

    private static string FindMainWindowXaml()
    {
        var probe = new DirectoryInfo(AppContext.BaseDirectory);
        while (probe is not null)
        {
            var candidate = Path.Combine(probe.FullName, "src", "zEClass", "MainWindow.xaml");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            probe = probe.Parent;
        }

        throw new FileNotFoundException(
            $"MainWindow.xaml not found above {AppContext.BaseDirectory}");
    }

    private static bool HasExplicitAutomationName(XElement control) =>
        control.Attributes().Any(a => a.Name.LocalName == "AutomationProperties.Name");

    /// <summary>
    /// Every Button/ToggleButton must offer at least one accessible-name source:
    /// an explicit AutomationProperties.Name (preferred, per the ChromeButton style note),
    /// a ToolTip, or text Content (UIA falls back to the content string). A control with
    /// none of these reaches a screen reader as a nameless rectangle.
    /// </summary>
    [Fact]
    public void EveryInteractiveControl_ExposesAnAccessibleName()
    {
        var doc = XDocument.Load(MainWindowXaml);
        var controls = doc.Descendants()
            .Where(e => e.Name.LocalName is "Button" or "ToggleButton")
            .ToList();

        Assert.True(controls.Count > 0, "expected interactive controls in MainWindow.xaml");

        var nameless = controls.Where(c =>
            !HasExplicitAutomationName(c) &&
            c.Attribute("ToolTip") is null &&
            (c.Attribute("Content") is not { Value: var content } ||
             string.IsNullOrWhiteSpace(content))).ToList();

        Assert.True(nameless.Count == 0,
            $"{nameless.Count} control(s) have no accessible name: " +
            string.Join(" | ", nameless.Select(c =>
                c.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Name")?.Value
                ?? c.Attribute("Content")?.Value
                ?? c.Name.LocalName)));
    }

    /// <summary>
    /// The window itself must be identifiable to automation, and the live status region
    /// must carry a LiveSetting so screen readers announce page/pen changes politely
    /// without interrupting the teacher.
    /// </summary>
    [Fact]
    public void MainWindowAndLiveStatusRegion_AreAnnouncedToAutomation()
    {
        var doc = XDocument.Load(MainWindowXaml);
        var root = doc.Root;
        Assert.NotNull(root);

        Assert.True(HasExplicitAutomationName(root!),
            "MainWindow needs an explicit AutomationProperties.Name");

        Assert.Contains(doc.Descendants(),
            e => e.Attributes().Any(a =>
                a.Name.LocalName == "AutomationProperties.LiveSetting"));
    }

    /// <summary>Icon-only buttons (no text content) must carry an explicit name or tooltip.</summary>
    [Fact]
    public void IconOnlyControls_CarryExplicitNameOrTooltip()
    {
        var doc = XDocument.Load(MainWindowXaml);
        var unnamed = doc.Descendants()
            .Where(e => e.Name.LocalName is "Button" or "ToggleButton")
            .Where(c => c.Attribute("Content") is null ||
                        string.IsNullOrWhiteSpace(c.Attribute("Content")?.Value))
            .Where(c => !HasExplicitAutomationName(c) && c.Attribute("ToolTip") is null)
            .ToList();

        Assert.True(unnamed.Count == 0,
            $"{unnamed.Count} icon-only control(s) without Name or ToolTip: " +
            string.Join(" | ", unnamed.Select(c =>
                c.Attribute("{http://schemas.microsoft.com/winfx/2006/xaml}Name")?.Value
                ?? c.Name.LocalName)));
    }
}
