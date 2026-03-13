using System.Text.Json;

namespace AdocNet.Cli;

internal static class ConfigLoader
{
    private const string ConfigFileName = "adocnet.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
    };

    public static ProjectConfig? Discover(string startDir)
    {
        var dir = Path.GetFullPath(startDir);
        while (dir is not null)
        {
            var configPath = Path.Combine(dir, ConfigFileName);
            if (File.Exists(configPath))
                return LoadFrom(configPath);
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    public static ProjectConfig? LoadFrom(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ProjectConfig>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
