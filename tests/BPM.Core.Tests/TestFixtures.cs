using System.Text.Json.Nodes;
using BPM.Core.Browsers;
using BPM.Core.Models;

namespace BPM.Core.Tests;

/// <summary>A throwaway folder under the temp directory that is deleted on dispose.</summary>
public sealed class TempDir : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("bpm-test-").FullName;

    public string Combine(params string[] parts) => Path.Combine([Root, .. parts]);

    public string WriteFile(string relativePath, string content)
    {
        var full = Combine(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    public string WriteJson(string relativePath, JsonNode json) => WriteFile(relativePath, json.ToJsonString());

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public static class TestFixtures
{
    public static BrowserInfo BrowserFor(string catalogId, string dataDir, bool running = false)
    {
        var definition = BrowserCatalog.Find(catalogId)
            ?? BrowserCatalog.All.First(d => d.Family == (catalogId == "firefox" ? BrowserFamily.Firefox : BrowserFamily.Chromium));
        return new BrowserInfo { Definition = definition, DataDir = dataDir, IsRunning = running };
    }

    public static ExtensionInfo Extension(
        ExtensionSource source = ExtensionSource.Store,
        string[]? api = null,
        string[]? hosts = null,
        string? storeUrl = "https://example.com/store") => new()
    {
        Id = "ext",
        Name = "Test extension",
        Source = source,
        ApiPermissions = api ?? [],
        HostPermissions = hosts ?? [],
        StoreUrl = storeUrl,
    };
}
