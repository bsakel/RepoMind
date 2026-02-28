using System.Text.Json;
using System.Text.RegularExpressions;
using Serilog;

namespace RepoMind.Scanner.Parsers;

public record ConfigEntry(string Source, string KeyName, string? DefaultValue, string FilePath);

public static partial class ConfigParser
{
    /// <summary>
    /// Scan a project directory for configuration keys from appsettings*.json and C# env var / IConfiguration usage.
    /// </summary>
    public static List<ConfigEntry> ScanProject(string projectDir)
    {
        var entries = new List<ConfigEntry>();

        // Scan appsettings*.json files
        var jsonFiles = Directory.GetFiles(projectDir, "appsettings*.json", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();

        foreach (var file in jsonFiles)
        {
            try
            {
                var relativePath = Path.GetRelativePath(projectDir, file).Replace('\\', '/');
                var json = File.ReadAllText(file);
                using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
                ExtractJsonKeys(doc.RootElement, "", relativePath, entries);
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to parse {File}: {Error}", file, ex.Message);
            }
        }

        // Scan C# files for env var and IConfiguration usage
        var csFiles = Directory.GetFiles(projectDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}test{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}"))
            .ToList();

        foreach (var file in csFiles)
        {
            try
            {
                var relativePath = Path.GetRelativePath(projectDir, file).Replace('\\', '/');
                var content = File.ReadAllText(file);
                ExtractCSharpConfigKeys(content, relativePath, entries);
            }
            catch (Exception ex) { Log.Warning("Failed to parse config in {File}: {Error}", file, ex.Message); }
        }

        return entries
            .DistinctBy(e => (e.KeyName, e.Source, e.FilePath))
            .ToList();
    }

    private static void ExtractJsonKeys(JsonElement element, string prefix, string filePath, List<ConfigEntry> entries)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var key = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}:{prop.Name}";
                    ExtractJsonKeys(prop.Value, key, filePath, entries);
                }
                break;

            case JsonValueKind.Array:
                // Skip arrays — not useful config keys
                break;

            default:
                var value = element.ToString();
                entries.Add(new ConfigEntry("appsettings", prefix, value, filePath));
                break;
        }
    }

    private static void ExtractCSharpConfigKeys(string content, string filePath, List<ConfigEntry> entries)
    {
        // Environment.GetEnvironmentVariable("KEY")
        foreach (Match m in EnvVarRegex().Matches(content))
        {
            entries.Add(new ConfigEntry("env_var", m.Groups[1].Value, null, filePath));
        }

        // IConfiguration["Key"] or Configuration["Key"] or config["Key"]
        foreach (Match m in ConfigIndexerRegex().Matches(content))
        {
            entries.Add(new ConfigEntry("IConfiguration", m.Groups[1].Value, null, filePath));
        }

        // GetSection("Key") or GetValue<T>("Key")
        foreach (Match m in GetSectionRegex().Matches(content))
        {
            entries.Add(new ConfigEntry("IConfiguration", m.Groups[1].Value, null, filePath));
        }
    }

    [GeneratedRegex(@"Environment\.GetEnvironmentVariable\(\s*""([^""]+)""\s*\)", RegexOptions.Compiled)]
    private static partial Regex EnvVarRegex();

    [GeneratedRegex(@"(?:onfiguration|config)\[""([^""]+)""\]", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex ConfigIndexerRegex();

    [GeneratedRegex(@"(?:GetSection|GetValue\s*<[^>]+>)\(\s*""([^""]+)""\s*\)", RegexOptions.Compiled)]
    private static partial Regex GetSectionRegex();
}
