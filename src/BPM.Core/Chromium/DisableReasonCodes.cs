using System.Text.Json.Nodes;
using BPM.Core.Util;

namespace BPM.Core.Chromium;

/// <summary>Chromium's extension disable-reason bits (extensions/browser/disable_reason.h, checked against Chromium main
/// in Oct 2026) in plain English.</summary>
public static class DisableReasonCodes
{
    public const long UserAction = 1 << 0;
    public const long PermissionsIncrease = 1 << 1;
    public const long Reload = 1 << 2;
    public const long UnsupportedRequirement = 1 << 3;
    public const long SideloadWipeout = 1 << 4;
    public const long UnknownFromSync = 1 << 5;
    public const long NotVerified = 1 << 8;
    public const long Greylist = 1 << 9;
    public const long Corrupted = 1 << 10;
    public const long RemoteInstall = 1 << 11;
    public const long ExternalExtension = 1 << 13;
    public const long UpdateRequiredByPolicy = 1 << 14;
    public const long CustodianApprovalRequired = 1 << 15;
    public const long BlockedByPolicy = 1 << 16;
    public const long Reinstall = 1 << 19;
    public const long NotAllowlisted = 1 << 20;
    public const long NotAshKeeplisted = 1 << 21;
    public const long PublishedInStoreRequired = 1 << 22;
    public const long UnsupportedManifestVersion = 1 << 23;
    public const long UnsupportedDeveloperExtension = 1 << 24;
    public const long UnknownReason = 1 << 25;
    public const long BlockedByCloudPolicyCheck = 1 << 26;
    public const long ByAnotherExtension = 1 << 27;

    static readonly (long Bit, string Text)[] Texts =
    [
        (UserAction, "turned off by you"),
        (PermissionsIncrease, "needs you to approve new permissions"),
        (Reload, "reloading"),
        (UnsupportedRequirement, "not supported on this computer"),
        (SideloadWipeout, "turned off because another program installed it"),
        (UnknownFromSync, "turned off on another device (sync)"),
        (NotVerified, "couldn't be verified by the store"),
        (Greylist, "flagged by the store as harmful or unwanted"),
        (Corrupted, "files are damaged and need repair"),
        (RemoteInstall, "installed remotely and needs approval"),
        (ExternalExtension, "added by another program and waiting for your approval"),
        (UpdateRequiredByPolicy, "must be updated (policy)"),
        (CustodianApprovalRequired, "waiting for parent approval"),
        (BlockedByPolicy, "blocked by policy"),
        (Reinstall, "being reinstalled"),
        (NotAllowlisted, "not on Safe Browsing's allowlist"),
        (NotAshKeeplisted, "not allowed on this device"),
        (PublishedInStoreRequired, "must be published in the store (policy)"),
        (UnsupportedManifestVersion, "uses the outdated Manifest V2 format"),
        (UnsupportedDeveloperExtension, "developer extensions aren't allowed while Developer mode is off"),
        (UnknownReason, "turned off for an unknown reason"),
        (BlockedByCloudPolicyCheck, "blocked by your organization's cloud policy check"),
        (ByAnotherExtension, "turned off by another extension"),
    ];

    /// <summary>Reads "disable_reasons", stored as a bit mask (older Chrome) or a list of reason values (newer Chrome).</summary>
    public static long ReadBits(JsonNode? raw)
    {
        if (raw is JsonArray list)
        {
            long bits = 0;
            foreach (var item in list) bits |= item.AsLong() ?? 0;
            return bits;
        }
        return raw.AsLong() ?? 0;
    }

    public static List<string> Describe(long bits)
    {
        var result = new List<string>();
        for (int i = 0; i < 40; i++)
        {
            long bit = 1L << i;
            if ((bits & bit) == 0) continue;
            var known = Texts.FirstOrDefault(t => t.Bit == bit);
            result.Add(known.Text ?? $"reason code {bit}");
        }
        return result;
    }
}
