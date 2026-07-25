using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Glue.Services;

/// <summary>
/// Persists which fold regions are collapsed, per file, across sessions.
/// Storage: a single JSON file under the user's local app data folder —
/// { "C:\path\to\file.cs": [123, 456] } where the numbers are the start
/// offsets of collapsed regions. Deliberately simple: this is not meant
/// to survive heavy edits that shift offsets a lot, just normal reopen-a-
/// file-later use.
/// </summary>
public static class FoldStateStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Glue", "foldstate.json");

    public static IReadOnlyList<int> GetCollapsedOffsets(string filePath)
    {
        var all = LoadAll();
        return all.TryGetValue(filePath, out var offsets) ? offsets : Array.Empty<int>();
    }

    public static void SetCollapsedOffsets(string filePath, IEnumerable<int> collapsedOffsets)
    {
        var all = LoadAll();
        all[filePath] = new List<int>(collapsedOffsets);
        SaveAll(all);
    }

    private static Dictionary<string, List<int>> LoadAll()
    {
        try
        {
            if (!File.Exists(StorePath)) return new Dictionary<string, List<int>>();
            var json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<Dictionary<string, List<int>>>(json)
                   ?? new Dictionary<string, List<int>>();
        }
        catch
        {
            // Corrupt or unreadable state file — start fresh rather than crash the IDE over this.
            return new Dictionary<string, List<int>>();
        }
    }

    private static void SaveAll(Dictionary<string, List<int>> all)
    {
        var dir = Path.GetDirectoryName(StorePath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(StorePath, json);
    }
}
