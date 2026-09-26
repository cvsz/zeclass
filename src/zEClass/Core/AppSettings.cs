using System;
using System.IO;
using System.Text.Json;

namespace zEClass.Core;

/// <summary>
/// The handful of persisted user preferences. One small JSON file under
/// <c>%LOCALAPPDATA%\zEClass\settings.json</c>: no registry, no elevation, and a corrupt file
/// falls back to defaults rather than breaking startup.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Start the NDI capture helper automatically when the board starts.</summary>
    public bool NdiAutoStart { get; set; } = true;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "zEClass", "settings.json");

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            CrashLog.Info($"Settings not loaded from {path}; using defaults. {ex.Message}");
        }

        return new AppSettings();
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            CrashLog.Info($"Settings not saved to {path}. {ex.Message}");
        }
    }
}
