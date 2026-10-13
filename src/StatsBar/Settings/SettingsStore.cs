using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StatsBar.Settings;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WinStats",
        "settings.json");

    public static AppSettings Load()
    {
        AdoptLegacyFile();
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var legacy = !json.Contains("\"density\"", StringComparison.OrdinalIgnoreCase);
                json = MigrateStyleNames(json);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings != null)
                {
                    settings.Modules = NormalizeModules(settings.Modules);
                    settings.CombinedModules = NormalizeCombined(settings.CombinedModules);
                    var migrated = CoerceLabeled(settings);
                    if (legacy)
                    {
                        settings.ApplyDensity(StripDensity.Minimal);
                        migrated = true;
                    }

                    if (migrated)
                        Save(settings);
                    return settings;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        var created = new AppSettings { Anchor = ReadLegacyAnchor() };
        Save(created);
        return created;
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        settings.Modules = NormalizeModules(settings.Modules);
        settings.CombinedModules = NormalizeCombined(settings.CombinedModules);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    private static void AdoptLegacyFile()
    {
        try
        {
            if (File.Exists(FilePath))
                return;
            var legacy = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "StatsBar",
                "settings.json");
            if (!File.Exists(legacy))
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.Copy(legacy, FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static StripAnchor ReadLegacyAnchor()
    {
        try
        {
            var legacy = Path.Combine(AppContext.BaseDirectory, "anchor.txt");
            if (!File.Exists(legacy))
                return StripAnchor.Left;

            return File.ReadAllText(legacy).Trim().Equals("Right", StringComparison.OrdinalIgnoreCase)
                ? StripAnchor.Right
                : StripAnchor.Left;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return StripAnchor.Left;
        }
    }

    private static string MigrateStyleNames(string json)
    {
        return json
            .Replace("\"textBar\"", "\"bar\"", StringComparison.Ordinal)
            .Replace("\"sparkline\"", "\"lineChart\"", StringComparison.Ordinal)
            .Replace("\"valueOnly\"", "\"mini\"", StringComparison.Ordinal)
            .Replace("\"text\"", "\"mini\"", StringComparison.Ordinal);
    }

    private static bool CoerceLabeled(AppSettings settings)
    {
        var unlabeled = settings.Modules.Any(module => module.Style == StripItemStyle.ValueOnly) || !settings.ShowLabels;
        settings.ShowLabels = true;
        foreach (var module in settings.Modules)
        {
            if (module.Style == StripItemStyle.ValueOnly)
                module.Style = StripItemStyle.Mini;
        }

        if (unlabeled)
            settings.ApplyDensity(settings.Density);
        return unlabeled;
    }

    private static List<ModuleId> NormalizeCombined(List<ModuleId>? modules)
    {
        var ordered = new List<ModuleId>();
        if (modules == null)
            return [ModuleId.Cpu, ModuleId.Ram, ModuleId.Gpu];

        foreach (var id in modules)
        {
            if (!Enum.IsDefined(id) || ordered.Contains(id))
                continue;
            ordered.Add(id);
        }

        return ordered.Count == 0 ? [ModuleId.Cpu, ModuleId.Ram, ModuleId.Gpu] : ordered;
    }

    private static List<ModuleSetting> NormalizeModules(List<ModuleSetting>? modules)
    {
        var byId = new Dictionary<ModuleId, ModuleSetting>();
        if (modules != null)
        {
            foreach (var module in modules)
            {
                if (!Enum.IsDefined(module.Id))
                    continue;
                byId[module.Id] = module;
            }
        }

        var ordered = new List<ModuleSetting>();
        foreach (var id in Enum.GetValues<ModuleId>())
        {
            if (byId.TryGetValue(id, out var existing))
                ordered.Add(existing);
            else
                ordered.Add(new ModuleSetting { Id = id });
        }

        return ordered;
    }
}
