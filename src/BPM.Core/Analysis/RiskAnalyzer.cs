using BPM.Core.Chromium;
using BPM.Core.Models;

namespace BPM.Core.Analysis;

/// <summary>Turns an extension's permissions and origin into a Low / Medium / High verdict with plain-English reasons.</summary>
public static class RiskAnalyzer
{
    public static RiskAssessment Assess(ExtensionInfo e)
    {
        var level = RiskLevel.Low;
        var reasons = new List<string>();

        void Raise(RiskLevel l, string why)
        {
            if (!reasons.Contains(why)) reasons.Add(why);
            if (l > level) level = l;
        }

        var hosts = PermissionCatalog.DescribeHosts(e.HostPermissions);
        if (hosts.Weight >= PermissionWeight.Medium) Raise(ToLevel(hosts.Weight), hosts.Description);

        foreach (var permission in e.ApiPermissions)
        {
            var info = PermissionCatalog.Describe(permission);
            if (info.Weight >= PermissionWeight.Medium) Raise(ToLevel(info.Weight), info.Description);
        }

        bool allSites = e.HostPermissions.Any(PermissionCatalog.IsAllSites);
        if (allSites && e.ApiPermissions.Contains("cookies", StringComparer.OrdinalIgnoreCase))
            Raise(RiskLevel.High, "Can read your sign-in cookies on every website");

        if (e.Source == ExtensionSource.Developer)
            Raise(RiskLevel.Medium, "Developer (unpacked) extension: no store has reviewed it");
        else if (e.Source is ExtensionSource.OtherProgram or ExtensionSource.Unknown && e.StoreUrl == null)
            Raise(RiskLevel.Medium, "Not installed from a store");

        if ((e.DisableReasonBits & DisableReasonCodes.Greylist) != 0)
            Raise(RiskLevel.High, "Flagged by the store as harmful or unwanted");

        if (e.ManifestVersion == 2 && e.Profile?.Browser?.Family == BrowserFamily.Chromium && !reasons.Contains(MV2Note))
            reasons.Add(MV2Note); // informational: doesn't raise the level

        return new RiskAssessment(level, reasons);
    }

    const string MV2Note = "Uses the outdated Manifest V2 format, which Chrome no longer supports";

    static RiskLevel ToLevel(PermissionWeight w) => w >= PermissionWeight.High ? RiskLevel.High : RiskLevel.Medium;
}
