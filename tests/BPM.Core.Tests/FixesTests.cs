using BPM.Core.Browsers;
using BPM.Core.Chromium;
using BPM.Core.Diagnostics;
using BPM.Core.Models;
using BPM.Core.Scanning;

namespace BPM.Core.Tests;

/// <summary>Regression tests for the Oct 2026 bug sweep.</summary>
public sealed class FixesTests
{
    // ---- disable reasons (values from Chromium's extensions/browser/disable_reason.h) ----

    [Theory]
    [InlineData(1L << 1, "needs you to approve new permissions")]
    [InlineData(1L << 8, "couldn't be verified by the store")]
    [InlineData(1L << 19, "being reinstalled")]
    [InlineData(1L << 23, "uses the outdated Manifest V2 format")]
    [InlineData(1L << 24, "developer extensions aren't allowed while Developer mode is off")]
    [InlineData(1L << 27, "turned off by another extension")]
    public void Describe_uses_chromium_bit_values(long bit, string text)
    {
        Assert.Equal([text], DisableReasonCodes.Describe(bit));
    }

    [Fact]
    public void Every_bit_chromium_currently_writes_has_a_plain_english_text()
    {
        // 1<<6, 1<<7, 1<<12, 1<<17 and 1<<18 are retired in Chromium and never written any more.
        long[] retired = [1L << 6, 1L << 7, 1L << 12, 1L << 17, 1L << 18];
        for (int i = 0; i < 28; i++)
        {
            long bit = 1L << i;
            if (retired.Contains(bit)) continue;
            Assert.DoesNotContain("reason code", DisableReasonCodes.Describe(bit)[0]);
        }
    }

    // ---- browser registrations ----

    [Fact]
    public void ExeFromCommand_without_exe_drops_the_arguments()
    {
        Assert.Equal(@"C:\Tools\browser", BrowserDetector.ExeFromCommand(@"C:\Tools\browser -url %1"));
    }

    // ---- live refresh ----

    static BrowserInfo Browser(bool enabled, string version = "1.0")
    {
        var browser = TestFixtures.BrowserFor("chrome", @"C:\chrome");
        var profile = new ProfileInfo { Key = "Default", Path = @"C:\chrome\Default", Name = "Me", Browser = browser };
        profile.Extensions.Add(new ExtensionInfo { Id = "abc", Name = "Ext", Version = version, Enabled = enabled, Profile = profile });
        browser.Profiles.Add(profile);
        return browser;
    }

    [Fact]
    public void Signature_is_the_same_for_identical_reads()
    {
        Assert.Equal(Scanner.Signature(Browser(true)), Scanner.Signature(Browser(true)));
    }

    [Fact]
    public void Signature_changes_when_an_extension_is_switched_or_updated()
    {
        var on = Scanner.Signature(Browser(true));
        Assert.NotEqual(on, Scanner.Signature(Browser(false)));
        Assert.NotEqual(on, Scanner.Signature(Browser(true, "2.0")));
    }

    [Fact]
    public void Signature_ignores_live_state()
    {
        var a = Browser(true);
        var b = Browser(true);
        b.IsRunning = true;
        b.Profiles[0].IsOpen = true;
        b.Profiles[0].SizeBytes = 123;
        Assert.Equal(Scanner.Signature(a), Scanner.Signature(b));
    }

    [Fact]
    public void Snapshot_has_its_own_browser_list()
    {
        var scan = new ScanResult();
        scan.Browsers.Add(Browser(true));
        var snapshot = scan.Snapshot();
        scan.Browsers[0] = Browser(false); // what a live refresh does
        Assert.True(snapshot.Browsers[0].Profiles[0].Extensions[0].Enabled);
    }

    [Fact]
    public void FillSizes_survives_the_scan_changing_while_it_runs()
    {
        using var dir = new TempDir();
        dir.WriteFile(@"Default\file.bin", "12345");
        var scan = new ScanResult();
        var browser = TestFixtures.BrowserFor("chrome", dir.Root);
        browser.Profiles.Add(new ProfileInfo { Key = "Default", Path = dir.Combine("Default"), Name = "Me", Browser = browser });
        browser.Profiles.Add(new ProfileInfo { Key = "Profile 1", Path = dir.Combine("Profile 1"), Name = "Two", Browser = browser });
        scan.Browsers.Add(browser);

        var profiles = scan.AllProfiles.ToList();
        Scanner.FillSizes(profiles, _ => scan.Browsers[0] = Browser(true)); // the list is replaced mid-way
        Assert.Equal(5, profiles[0].SizeBytes);
        Assert.Equal(0, profiles[1].SizeBytes);
    }

    // ---- doctor ----

    [Fact]
    public void Claude_ids_include_extensions_the_claude_app_link_accepts()
    {
        var newBuild = new string('b', 32);
        var hosts = new[]
        {
            new NativeHost("com.anthropic.claude_browser_extension", RegistryScope.User, "k", "m.json", "host.exe",
                [newBuild, "not-an-id"], ["Google Chrome"], HostStatus.Ok),
            new NativeHost("com.other.app", RegistryScope.User, "k2", "m2.json", "other.exe",
                [new string('c', 32)], ["Google Chrome"], HostStatus.Ok),
        };
        var ids = ConnectionDoctor.ClaudeIdsFor(hosts);
        Assert.Contains(newBuild, ids);
        Assert.Contains("fcoeoabgfenejglbffodgkkbkcdhcgfn", ids);
        Assert.DoesNotContain("not-an-id", ids);
        Assert.DoesNotContain(new string('c', 32), ids);
    }

    // ---- scanning robustness ----

    [Fact]
    public void A_bad_profiles_ini_entry_skips_only_that_profile()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.Combine("good"));
        dir.WriteFile("profiles.ini", "[Profile0]\nName=bad\nIsRelative=0\nPath=C:\\bad\0path\n\n[Profile1]\nName=good\nIsRelative=1\nPath=good\n");
        var browser = TestFixtures.BrowserFor("firefox", dir.Root);
        Scanner.ReadProfiles(browser);
        Assert.Null(browser.ScanError);
        Assert.Equal(["good"], browser.Profiles.Select(p => p.Name));
    }
}
