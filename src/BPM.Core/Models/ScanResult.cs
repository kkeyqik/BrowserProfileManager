using System.Text.Json.Serialization;

namespace BPM.Core.Models;

/// <summary>Everything one scan found on this PC.</summary>
public sealed class ScanResult
{
    public DateTime ScannedAt { get; init; } = DateTime.Now;
    public string MachineName { get; init; } = Environment.MachineName;
    public string UserName { get; init; } = Environment.UserName;

    public List<BrowserInfo> Browsers { get; } = [];

    /// <summary>Browsers registered with Windows that aren't in the catalog (listed, not managed).</summary>
    public List<OtherBrowser> OtherBrowsers { get; } = [];

    /// <summary>A copy with its own browser list (same browser objects), for background work while the app may swap a
    /// browser in the original after a live refresh. A browser's profiles aren't changed once it is in a scan.</summary>
    public ScanResult Snapshot()
    {
        var copy = new ScanResult { ScannedAt = ScannedAt, MachineName = MachineName, UserName = UserName };
        copy.Browsers.AddRange(Browsers);
        copy.OtherBrowsers.AddRange(OtherBrowsers);
        return copy;
    }

    [JsonIgnore] public IEnumerable<ProfileInfo> AllProfiles => Browsers.SelectMany(b => b.Profiles);
    [JsonIgnore] public IEnumerable<ExtensionInfo> AllExtensions => AllProfiles.SelectMany(p => p.Extensions);
}
