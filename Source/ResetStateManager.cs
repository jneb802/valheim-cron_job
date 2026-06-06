using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;

namespace CronJob;

public static class ResetStateManager
{
  private const string FileName = "praetoris_resets.json";
  private static readonly string FilePath = Path.Combine(Paths.ConfigPath, FileName);
  private static readonly List<ResetSpec> Specs =
  [
    new("meadows_locations", "Meadows Location Reset", "location", "meadows", "", TimeSpan.FromDays(3), "locations_reset", "WoodHouse1"),
    new("blackforest_locations", "Black Forest Location Reset", "location", "blackforest", "", TimeSpan.FromDays(3), "locations_reset", "Ruin1"),
    new("swamp_locations", "Swamp Location Reset", "location", "swamp", "", TimeSpan.FromDays(3), "locations_reset", "SwampHut1"),
    new("mountain_locations", "Mountain Location Reset", "location", "mountain", "", TimeSpan.FromDays(3), "locations_reset", "MountainWell1"),
    new("plains_locations", "Plains Location Reset", "location", "plains", "", TimeSpan.FromDays(3), "locations_reset", "GoblinCamp2"),
    new("mistlands_locations", "Mistlands Location Reset", "location", "mistlands", "", TimeSpan.FromDays(3), "locations_reset", "Mistlands_RockSpire1"),
    new("ashlands_locations", "Ashlands Location Reset", "location", "ashlands", "", TimeSpan.FromDays(3), "locations_reset", "CharredFortress"),
    new("blackforest_dungeons", "Black Forest Dungeon Reset", "dungeon", "blackforest", "", TimeSpan.FromDays(3), "locations_reset", "Hildir_crypt"),
    new("swamp_dungeons", "Swamp Crypt Reset", "dungeon", "swamp", "", TimeSpan.FromDays(3), "locations_reset", "SunkenCrypt4"),
    new("ashlands_forts", "Ashlands Fort Reset", "dungeon", "ashlands", "", TimeSpan.FromDays(3), "locations_reset", "MWL_AshlandsFort"),
    new("vegetation", "Meadows Vegetation Reset", "vegetation", "meadows", "meadows", TimeSpan.FromHours(12), "vegetation_reset", "Pickable_Stone"),
    new("tin", "Tin Node Reset", "vegetation", "blackforest", "tin", TimeSpan.FromHours(24), "vegetation_reset", "MineRock_Tin"),
    new("obsidian", "Obsidian Node Reset", "vegetation", "mountain", "obsidian", TimeSpan.FromHours(24), "vegetation_reset", "MineRock_Obsidian"),
    new("wild_plants", "Wild Plant Reset", "vegetation", "", "wild_plants", TimeSpan.FromHours(24), "vegetation_reset", "Pickable_Turnip", "Pickable_Carrot"),
    new("copper", "Copper Node Reset", "vegetation", "blackforest", "copper", TimeSpan.FromDays(3), "vegetation_reset", "rock4_copper"),
    new("silver", "Silver Vein Reset", "vegetation", "mountain", "silver", TimeSpan.FromDays(3), "vegetation_reset", "silvervein"),
    new("oak", "Oak Tree Reset", "vegetation", "meadows", "oak", TimeSpan.FromDays(3), "vegetation_reset", "Oak1"),
    new("leviathan", "Leviathan Reset", "vegetation", "ocean", "leviathan", TimeSpan.FromDays(3), "vegetation_reset", "Leviathan"),
  ];

  private static readonly Dictionary<string, ResetRecord> Records = Specs.ToDictionary(
    spec => spec.Key,
    spec => new ResetRecord(spec));
  private static bool Loaded;

  public static void TrackExecutedCommand(string command, DateTime last, DateTime? next)
  {
    var specs = Specs.Where(spec => spec.IsMatch(command)).ToList();
    if (specs.Count == 0) return;

    Load();

    foreach (var spec in specs)
    {
      var record = Records[spec.Key];
      record.Last = Normalize(last);
      record.Next = next.HasValue ? Normalize(next.Value) : record.Last.Value.Add(spec.Interval);
    }

    Write();
  }

  public static void EnsureFile()
  {
    Load();
    if (!File.Exists(FilePath))
      Write();
  }

  private static void Load()
  {
    if (Loaded) return;
    Loaded = true;

    if (!File.Exists(FilePath)) return;

    try
    {
      foreach (var spec in Specs)
      {
        var record = Records[spec.Key];
        record.Last = ReadDateTime(spec.Key, "last");
        record.Next = ReadDateTime(spec.Key, "next");
      }
    }
    catch (Exception e)
    {
      CronJob.Log.LogWarning($"Failed to load {FileName}: {e.Message}");
    }
  }

  private static void Write()
  {
    try
    {
      Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
      var tmp = FilePath + ".tmp";
      File.WriteAllText(tmp, BuildJson());
      if (File.Exists(FilePath))
        File.Delete(FilePath);
      File.Move(tmp, FilePath);
    }
    catch (Exception e)
    {
      CronJob.Log.LogWarning($"Failed to write {FileName}: {e.Message}");
    }
  }

  private static DateTime Normalize(DateTime value)
  {
    return value.Kind switch
    {
      DateTimeKind.Utc => value,
      DateTimeKind.Local => value.ToUniversalTime(),
      _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
  }

  private static string BuildJson()
  {
    var builder = new StringBuilder();
    builder.AppendLine("{");
    builder.AppendLine($"  \"generated_at\": \"{Escape(Format(DateTime.UtcNow))}\",");
    builder.AppendLine("  \"resets\": {");

    var records = Records.ToList();
    for (var i = 0; i < records.Count; ++i)
    {
      var pair = records[i];
      var comma = i == records.Count - 1 ? "" : ",";
      builder.AppendLine($"    \"{Escape(pair.Key)}\": {{");
      builder.AppendLine($"      \"label\": \"{Escape(pair.Value.Label)}\",");
      builder.AppendLine($"      \"category\": \"{Escape(pair.Value.Category)}\",");
      builder.AppendLine($"      \"biome\": \"{Escape(pair.Value.Biome)}\",");
      builder.AppendLine($"      \"vegetation\": \"{Escape(pair.Value.Vegetation)}\",");
      builder.AppendLine($"      \"last\": {FormatJsonString(pair.Value.Last)},");
      builder.AppendLine($"      \"next\": {FormatJsonString(pair.Value.Next)},");
      builder.AppendLine($"      \"interval_seconds\": {(int)pair.Value.Interval.TotalSeconds}");
      builder.AppendLine($"    }}{comma}");
    }

    builder.AppendLine("  }");
    builder.AppendLine("}");
    return builder.ToString();
  }

  private static DateTime? ReadDateTime(string resetKey, string field)
  {
    var raw = File.ReadAllText(FilePath);
    var resetMatch = Regex.Match(
      raw,
      $"\"{Regex.Escape(resetKey)}\"\\s*:\\s*\\{{(?<body>.*?)\\}}",
      RegexOptions.Singleline);
    if (!resetMatch.Success) return null;

    var fieldMatch = Regex.Match(
      resetMatch.Groups["body"].Value,
      $"\"{Regex.Escape(field)}\"\\s*:\\s*(?:\"(?<value>[^\"]*)\"|null)",
      RegexOptions.Singleline);
    if (!fieldMatch.Success) return null;

    var text = fieldMatch.Groups["value"].Value;
    if (string.IsNullOrWhiteSpace(text)) return null;
    return DateTime.TryParse(
      text,
      null,
      System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
      out var parsed)
      ? parsed.ToUniversalTime()
      : null;
  }

  private static string FormatJsonString(DateTime? value)
  {
    var formatted = Format(value);
    return formatted == null ? "null" : $"\"{Escape(formatted)}\"";
  }

  private static string? Format(DateTime? value) => value.HasValue ? Format(value.Value) : null;
  private static string Format(DateTime value) => Normalize(value).ToString("O");
  private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

  private sealed class ResetSpec(string key, string label, string category, string biome, string vegetation, TimeSpan interval, params string[] matchParts)
  {
    public string Key { get; } = key;
    public string Label { get; } = label;
    public string Category { get; } = category;
    public string Biome { get; } = biome;
    public string Vegetation { get; } = vegetation;
    public TimeSpan Interval { get; } = interval;
    private string[] MatchParts { get; } = matchParts;

    public bool IsMatch(string command) =>
      MatchParts.All(part => command.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0);
  }

  private sealed class ResetRecord(ResetSpec spec)
  {
    public string Label { get; } = spec.Label;
    public string Category { get; } = spec.Category;
    public string Biome { get; } = spec.Biome;
    public string Vegetation { get; } = spec.Vegetation;
    public TimeSpan Interval { get; } = spec.Interval;
    public DateTime? Last { get; set; }
    public DateTime? Next { get; set; }

  }
}
