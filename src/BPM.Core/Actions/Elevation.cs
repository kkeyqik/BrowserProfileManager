using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BPM.Core.Browsers;
using Microsoft.Win32;

namespace BPM.Core.Actions;

public enum RegistryOpKind { SetString, DeleteValue, DeleteKey, CreateKey }

/// <summary>One registry change. <see cref="Values"/> is used by CreateKey to restore a deleted key.
/// Keys are written without "WOW6432Node"; set <see cref="Wow64_32"/> for the 32-bit view instead.</summary>
public sealed record RegistryOperation(
    RegistryOpKind Op,
    string Key,
    string? Name = null,
    string? Value = null,
    bool Wow64_32 = false,
    Dictionary<string, string>? Values = null);

public sealed class ElevatedRequest
{
    public List<RegistryOperation> Operations { get; init; } = [];

    /// <summary>Ask Windows to refresh machine Group Policy afterwards, so browsers reload their policies.</summary>
    public bool RefreshMachinePolicy { get; init; }
}

public sealed class ElevatedResponse
{
    public bool Ok { get; set; }
    public bool Cancelled { get; set; }
    public string? Error { get; set; }
}

/// <summary>Does the registry work for both the normal app (current user) and the elevated copy (HKLM).
/// Only these changes are ever allowed:
///  • set / delete the "ExtensionSettings" value of a catalog browser's own policy key;
///  • delete / recreate one entry directly under a browser's sideload (Extensions) or native-messaging key.</summary>
public static class ElevatedExecutor
{
    /// <summary>Exit codes of the elevated copy (it answers only through its exit code, never by writing files).</summary>
    public const int ExitOk = 0, ExitRefused = 2, ExitFailed = 3, ExitBadRequest = 4, ExitUnexpected = 5;

    static readonly JsonSerializerOptions Options = new() { Converters = { new JsonStringEnumConverter() } };

    [DllImport("userenv.dll", SetLastError = true)]
    static extern bool RefreshPolicyEx(bool machine, uint options);

    const uint RefreshForce = 1;
    public const int MaxEncodedLength = 30000; // stays well inside the Windows command-line limit

    /// <summary>Checks one operation against the allowlist (op kind, value name and exact key shape).</summary>
    public static bool IsAllowed(RegistryOperation op)
    {
        if (Canonical(op.Key) is not { } key) return false;
        return op.Op switch
        {
            RegistryOpKind.SetString or RegistryOpKind.DeleteValue =>
                op.Name == ExtensionSettingsPolicy.ValueName && IsPolicyKey(key),
            RegistryOpKind.DeleteKey or RegistryOpKind.CreateKey =>
                IsDirectChildOfExtensionOrHostKey(key),
            _ => false,
        };
    }

    /// <summary>The key with validated components, or null when it has empty parts, control characters,
    /// forward slashes, "." / ".." or an explicit WOW6432Node.</summary>
    internal static string? Canonical(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.StartsWith('\\') || key.EndsWith('\\')) return null;
        var parts = key.Split('\\');
        foreach (var part in parts)
        {
            if (part.Length == 0 || part is "." or ".." || part.Any(c => char.IsControl(c) || c == '/')) return null;
            if (part.Equals("WOW6432Node", StringComparison.OrdinalIgnoreCase)) return null;
        }
        return string.Join('\\', parts);
    }

    static bool IsPolicyKey(string key) =>
        BrowserCatalog.All.Any(b => b.PolicyKey != null && key.Equals(b.PolicyKey, StringComparison.OrdinalIgnoreCase));

    static bool IsDirectChildOfExtensionOrHostKey(string key) =>
        BrowserCatalog.All.Any(b =>
            (b.ExternalExtensionsKey != null && IsDirectChild(key, b.ExternalExtensionsKey))
            || b.NativeHostKeys.Any(hosts => IsDirectChild(key, hosts)));

    static bool IsDirectChild(string key, string parent) =>
        key.StartsWith(parent + @"\", StringComparison.OrdinalIgnoreCase) && key.IndexOf('\\', parent.Length + 1) < 0;

    /// <summary>Compressed, base64 request for the elevated copy's command line.</summary>
    public static string EncodeRequest(ElevatedRequest request)
    {
        using var buffer = new MemoryStream();
        using (var deflate = new DeflateStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
            JsonSerializer.Serialize(deflate, request, Options);
        return Convert.ToBase64String(buffer.ToArray());
    }

    public static ElevatedRequest DecodeRequest(string encoded)
    {
        using var deflate = new DeflateStream(new MemoryStream(Convert.FromBase64String(encoded)), CompressionMode.Decompress);
        return JsonSerializer.Deserialize<ElevatedRequest>(deflate, Options) ?? throw new InvalidDataException("Empty request.");
    }

    /// <summary>Entry point for "--elevated &lt;request&gt;" in the elevated copy. Returns the process exit code.</summary>
    public static int RunFromCommandLine(string encodedRequest)
    {
        ElevatedRequest request;
        try
        {
            request = DecodeRequest(encodedRequest);
        }
        catch (Exception e) when (e is FormatException or JsonException or InvalidDataException or IOException)
        {
            return ExitBadRequest;
        }
        try
        {
            if (request.Operations.Count == 0 || request.Operations.Any(op => !IsAllowed(op))) return ExitRefused;
            foreach (var op in request.Operations) Apply(op, RegistryHive.LocalMachine);
            if (request.RefreshMachinePolicy) RefreshPolicyEx(true, RefreshForce);
            return ExitOk;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException or ArgumentException)
        {
            return ExitFailed;
        }
        catch (Exception)
        {
            return ExitUnexpected; // never let the elevated copy crash with an unhandled exception
        }
    }

    /// <summary>Applies an operation to the current user's registry (no elevation needed), with the same allowlist.</summary>
    public static void ApplyForCurrentUser(RegistryOperation op)
    {
        if (!IsAllowed(op)) throw new UnauthorizedAccessException($"Refused to change {op.Key}: not a browser setting.");
        Apply(op, RegistryHive.CurrentUser);
    }

    static void Apply(RegistryOperation op, RegistryHive hive)
    {
        var key = Canonical(op.Key)!;
        using var root = RegistryKey.OpenBaseKey(hive, op.Wow64_32 ? RegistryView.Registry32 : RegistryView.Registry64);
        switch (op.Op)
        {
            case RegistryOpKind.SetString:
                using (var k = root.CreateSubKey(key, writable: true))
                    k.SetValue(op.Name!, op.Value ?? "", RegistryValueKind.String);
                break;
            case RegistryOpKind.DeleteValue:
                using (var k = root.OpenSubKey(key, writable: true))
                    k?.DeleteValue(op.Name!, throwOnMissingValue: false);
                DeleteIfEmpty(root, key);
                break;
            case RegistryOpKind.DeleteKey:
                root.DeleteSubKeyTree(key, throwOnMissingSubKey: false);
                break;
            case RegistryOpKind.CreateKey:
                using (var k = root.CreateSubKey(key, writable: true))
                    foreach (var (name, value) in op.Values ?? []) k.SetValue(name, value, RegistryValueKind.String);
                break;
        }
    }

    /// <summary>Removes a policy key the app emptied (and empty parents up to SOFTWARE\Policies), so the browser
    /// stops showing "Managed by your organization". Keys that still hold anything are left alone.</summary>
    static void DeleteIfEmpty(RegistryKey root, string keyPath)
    {
        using (var key = root.OpenSubKey(keyPath))
        {
            if (key == null || key.ValueCount > 0 || key.SubKeyCount > 0) return;
        }
        root.DeleteSubKey(keyPath, throwOnMissingSubKey: false);
        var parent = Path.GetDirectoryName(keyPath);
        if (parent != null && parent.StartsWith(@"SOFTWARE\Policies\", StringComparison.OrdinalIgnoreCase)) DeleteIfEmpty(root, parent);
    }
}

/// <summary>Starts the elevated copy of the app (one UAC prompt) and waits for its exit code.</summary>
public static class ElevatedRunner
{
    public static async Task<ElevatedResponse> RunAsync(string appExePath, ElevatedRequest request)
    {
        var refused = request.Operations.FirstOrDefault(op => !ElevatedExecutor.IsAllowed(op));
        if (refused != null) return new ElevatedResponse { Error = $"Refused to change {refused.Key}: not a browser setting." };

        var encoded = ElevatedExecutor.EncodeRequest(request);
        if (encoded.Length > ElevatedExecutor.MaxEncodedLength) return new ElevatedResponse { Error = "The change is too large to pass to the administrator helper." };

        var start = new ProcessStartInfo(appExePath)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            Arguments = $"--elevated {encoded}",
        };
        try
        {
            using var process = Process.Start(start) ?? throw new InvalidOperationException("The administrator helper didn't start.");
            await process.WaitForExitAsync();
            return process.ExitCode switch
            {
                ElevatedExecutor.ExitOk => new ElevatedResponse { Ok = true },
                ElevatedExecutor.ExitRefused => new ElevatedResponse { Error = "The administrator helper refused the change (not a browser setting)." },
                ElevatedExecutor.ExitFailed => new ElevatedResponse { Error = "Windows didn't allow the registry change." },
                ElevatedExecutor.ExitBadRequest => new ElevatedResponse { Error = "The administrator helper couldn't read the request." },
                _ => new ElevatedResponse { Error = $"The administrator helper stopped unexpectedly (code {process.ExitCode})." },
            };
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 1223)
        {
            return new ElevatedResponse { Cancelled = true, Error = "You declined the administrator prompt." };
        }
    }
}
