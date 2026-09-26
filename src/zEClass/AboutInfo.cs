using System;
using System.Reflection;

namespace zEClass;

/// <summary>
/// The facts shown in the About dialog, kept as a pure model so the wording is unit tested.
/// The dialog itself is just a renderer: if the text is wrong, a test fails rather than a
/// teacher reading a wrong credit line in front of a class.
/// </summary>
public sealed record AboutInfo(
    string ProductName,
    string Version,
    string Developer,
    string Copyright,
    string Provenance)
{
    public static AboutInfo Current => new(
        ProductName: "zEClass Interactive Whiteboard",
        Version: ReadVersion(),
        Developer: "ZEAZDEV COMPANY LIMITED",
        Copyright: $"© {DateTime.Now.Year} ZEAZDEV COMPANY LIMITED",
        Provenance: "A clean-room whiteboard. Contains no vendor code.");

    private static string ReadVersion()
    {
        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
        catch (Exception)
        {
            // Version text must never take the About dialog down with it.
            return "1.0.0";
        }
    }
}
