using System.Text.Json.Serialization;

namespace AdocNet.Cli;

internal sealed class ProjectConfig
{
    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("outDir")]
    public string? OutDir { get; set; }

    [JsonPropertyName("recursive")]
    public bool? Recursive { get; set; }

    [JsonPropertyName("styled")]
    public bool? Styled { get; set; }

    [JsonPropertyName("theme")]
    public string? Theme { get; set; }

    [JsonPropertyName("attributes")]
    public Dictionary<string, string>? Attributes { get; set; }

    [JsonPropertyName("allowUriRead")]
    public bool? AllowUriRead { get; set; }

    [JsonPropertyName("includeMaxDepth")]
    public int? IncludeMaxDepth { get; set; }
}
