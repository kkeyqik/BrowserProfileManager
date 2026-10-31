using BPM.Core.Models;
using Microsoft.Win32;

namespace BPM.Core.Diagnostics;

/// <summary>One browser policy found in the registry. Any of these makes the browser say "Managed by your organization".</summary>
public sealed record PolicyValue(string Browsers, RegistryScope Scope, string Key, string Name, string Value)
{
    public string DisplayKey => Scope.Display(Key);
}

public static class PolicyInspector
{
    public static List<PolicyValue> Read(IEnumerable<BrowserDefinition> browsers)
    {
        var list = new List<PolicyValue>();
        // Chrome's channels share one policy key: list each key once with every browser that reads it.
        foreach (var group in browsers.Where(b => b.PolicyKey != null).GroupBy(b => b.PolicyKey!, StringComparer.OrdinalIgnoreCase))
        {
            var names = string.Join(", ", group.Select(b => b.Name));
            foreach (var scope in new[] { RegistryScope.Machine, RegistryScope.User })
            {
                try
                {
                    using var root = scope.OpenBase();
                    using var key = root.OpenSubKey(group.Key);
                    if (key == null) continue;
                    foreach (var name in key.GetValueNames())
                        list.Add(new PolicyValue(names, scope, group.Key, name.Length == 0 ? "(default)" : name, Format(key.GetValue(name))));
                    foreach (var listName in key.GetSubKeyNames())
                    {
                        using var sub = key.OpenSubKey(listName);
                        var items = sub?.GetValueNames().Select(n => Format(sub.GetValue(n))) ?? [];
                        list.Add(new PolicyValue(names, scope, $@"{group.Key}\{listName}", listName, string.Join(", ", items)));
                    }
                }
                catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
                {
                    // Unreadable: skip.
                }
            }
        }
        return list;
    }

    /// <summary>Values of one list-style policy (e.g. NativeMessagingBlocklist), from machine and user scope.</summary>
    public static List<string> ReadList(string policyKey, string listName)
    {
        var items = new List<string>();
        foreach (var scope in new[] { RegistryScope.Machine, RegistryScope.User })
        {
            try
            {
                using var root = scope.OpenBase();
                using var key = root.OpenSubKey($@"{policyKey}\{listName}");
                if (key != null) items.AddRange(key.GetValueNames().Select(n => Format(key.GetValue(n))));
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // Unreadable: skip this scope.
            }
        }
        return items;
    }

    /// <summary>A single-value policy, machine scope first (it wins over user scope).</summary>
    public static object? ReadValue(string policyKey, string name)
    {
        foreach (var scope in new[] { RegistryScope.Machine, RegistryScope.User })
        {
            try
            {
                using var root = scope.OpenBase();
                using var key = root.OpenSubKey(policyKey);
                if (key?.GetValue(name) is { } value) return value;
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // Unreadable: try the next scope.
            }
        }
        return null;
    }

    static string Format(object? value) => value switch
    {
        string[] multi => string.Join(", ", multi),
        byte[] bytes => Convert.ToHexString(bytes),
        null => "",
        _ => value.ToString() ?? "",
    };
}
