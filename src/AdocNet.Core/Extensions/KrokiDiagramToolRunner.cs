using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace AdocNet.Extensions;

/// <summary>
/// Generates diagrams via the Kroki HTTP API (https://kroki.io).
/// Implements <see cref="IDiagramToolRunner"/> as an opt-in alternative to local tool execution.
/// Supports all diagram languages that Kroki supports (PlantUML, Mermaid, Graphviz, etc.).
/// </summary>
public sealed class KrokiDiagramToolRunner : IDiagramToolRunner
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private bool? _isAvailable;

    /// <summary>
    /// Initializes the Kroki runner.
    /// </summary>
    /// <param name="baseUrl">The Kroki service URL. Defaults to the public instance.</param>
    /// <param name="httpClient">Optional HttpClient instance. If null, a default is created.</param>
    public KrokiDiagramToolRunner(string baseUrl = "https://kroki.io", HttpClient? httpClient = null)
    {
        _baseUrl = (baseUrl ?? throw new ArgumentNullException(nameof(baseUrl))).TrimEnd('/');
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <inheritdoc />
    /// <remarks>
    /// Checks connectivity by sending a GET request to the base URL.
    /// The result is cached for the lifetime of this runner instance.
    /// </remarks>
    public bool IsAvailable
    {
        get
        {
            if (_isAvailable.HasValue)
                return _isAvailable.Value;

            try
            {
                var response = _httpClient.GetAsync(_baseUrl).GetAwaiter().GetResult();
                _isAvailable = response.IsSuccessStatusCode;
            }
            catch
            {
                _isAvailable = false;
            }

            return _isAvailable.Value;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// POSTs the diagram source to Kroki and saves the resulting PNG image.
    /// Returns null if the HTTP request fails or the response is not successful.
    /// </remarks>
    public string? Generate(string language, string source, string outputDirectory)
    {
        if (language is null) throw new ArgumentNullException(nameof(language));
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (outputDirectory is null) throw new ArgumentNullException(nameof(outputDirectory));

        Directory.CreateDirectory(outputDirectory);

        var hash = ComputeHash(source);
        var outputPath = Path.Combine(outputDirectory, $"{hash}.png");

        // If already generated with same hash, reuse
        if (File.Exists(outputPath))
            return outputPath;

        var url = $"{_baseUrl}/{language}/png";

        try
        {
            var content = new StringContent(source, Encoding.UTF8, "text/plain");
            var response = _httpClient.PostAsync(url, content).GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
                return null;

            var imageBytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            File.WriteAllBytes(outputPath, imageBytes);
            return outputPath;
        }
        catch
        {
            return null;
        }
    }

    private static string ComputeHash(string input)
    {
#if NET5_0_OR_GREATER
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
#else
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
#endif
        return BitConverter.ToString(bytes, 0, 8).Replace("-", "").ToLowerInvariant();
    }
}
