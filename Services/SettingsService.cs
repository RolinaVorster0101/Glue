using System;
using System.IO;
using System.Text.Json;

namespace Glue.Services;

/// <summary>
/// Glue's persisted preferences. Deliberately scoped to what's actually
/// functional today — editor font/indentation and Format on Save. The
/// roadmap describes a much bigger Settings page eventually (theme, LSP
/// paths, Advisory Layer, Git/GitHub, Secrets — docs/ROADMAP.md section
/// 2.15), but most of those don't have real functionality behind them yet.
/// This is meant to grow as those features actually land, not to have
/// placeholder settings for things that don't do anything.
/// </summary>
public class GlueSettings
{
    public string FontFamily { get; set; } = "Cascadia Code,Consolas,Menlo,monospace";
    public double FontSize { get; set; } = 14;
    public int IndentationSize { get; set; } = 4;
    public bool ConvertTabsToSpaces { get; set; } = true;
    public bool FormatOnSave { get; set; } = false;
}

/// <summary>
/// Loads/saves GlueSettings as JSON under %APPDATA%\Glue\settings.json —
/// same storage pattern as FoldStateStore.
/// </summary>
public static class SettingsService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Glue", "settings.json");

    public static GlueSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new GlueSettings();
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<GlueSettings>(json) ?? new GlueSettings();
        }
        catch
        {
            // Corrupt or unreadable settings file — start with defaults
            // rather than crash the IDE over this.
            return new GlueSettings();
        }
    }

    public static void Save(GlueSettings settings)
    {
        var dir = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }
}
