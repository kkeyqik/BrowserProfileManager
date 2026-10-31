using Microsoft.Win32;

namespace BPM.Core.Diagnostics;

/// <summary>Where a registry entry lives: current user, machine (64-bit view) or machine (32-bit view, WOW6432Node).</summary>
public sealed record RegistryScope(RegistryHive Hive, RegistryView View, string Label)
{
    public static readonly RegistryScope User = new(RegistryHive.CurrentUser, RegistryView.Default, "You only");
    public static readonly RegistryScope Machine = new(RegistryHive.LocalMachine, RegistryView.Registry64, "All users");
    public static readonly RegistryScope Machine32 = new(RegistryHive.LocalMachine, RegistryView.Registry32, "All users (32-bit)");
    public static readonly IReadOnlyList<RegistryScope> All = [User, Machine, Machine32];

    public bool IsMachine => Hive == RegistryHive.LocalMachine;
    public bool IsWow32 => View == RegistryView.Registry32;

    /// <summary>Path as Regedit shows it, e.g. HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Google\Chrome\Extensions\…</summary>
    public string Display(string key)
    {
        var hive = Hive == RegistryHive.LocalMachine ? "HKEY_LOCAL_MACHINE" : "HKEY_CURRENT_USER";
        var path = IsWow32 && key.StartsWith(@"SOFTWARE\", StringComparison.OrdinalIgnoreCase) ? @"SOFTWARE\WOW6432Node\" + key[9..] : key;
        return $@"{hive}\{path}";
    }

    public RegistryKey OpenBase() => RegistryKey.OpenBaseKey(Hive, View);
}
