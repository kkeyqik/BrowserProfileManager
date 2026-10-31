using BPM.Core.Diagnostics;
using BPM.Core.Models;

namespace BPM.Core.Tests;

public sealed class HealthCheckerTests
{
    static readonly DateTime Now = new(2026, 10, 31, 12, 0, 0);

    static ScanResult Scan(params (string Key, string Name, string? Email, int DaysAgo, bool Open)[] profiles)
    {
        var scan = new ScanResult();
        var chrome = TestFixtures.BrowserFor("chrome", @"C:\chrome");
        foreach (var (key, name, email, daysAgo, open) in profiles)
            chrome.Profiles.Add(new ProfileInfo
            {
                Key = key, Path = $@"C:\chrome\{key}", Name = name, Email = email, Browser = chrome,
                LastActive = Now.AddDays(-daysAgo), IsOpen = open,
            });
        scan.Browsers.Add(chrome);
        return scan;
    }

    static List<DoctorCheck> Run(ScanResult scan, Dictionary<string, string>? labels = null, IReadOnlyList<PolicyValue>? policies = null, IReadOnlyList<NativeHost>? hosts = null) =>
        HealthChecker.Run(scan, hosts ?? [], policies ?? [], labels ?? [], Now);

    [Fact]
    public void A_paused_account_in_an_open_profile_is_a_warning_with_an_open_fix()
    {
        var scan = Scan(("Profile 5", "Asha", "asha@example.com", 0, true), ("Default", "Work", "work@example.com", 0, true));
        var checks = Run(scan, new() { [@"C:\chrome\Profile 5"] = "Paused", [@"C:\chrome\Default"] = "Work" });
        var paused = Assert.Single(checks, c => c.Status == CheckStatus.Warning);
        Assert.Equal(HealthChecker.Accounts, paused.Group);
        Assert.Contains("Asha", paused.Title);
        Assert.Equal(FixKind.OpenProfile, paused.Fix);
        Assert.Equal("chrome\nProfile 5", paused.FixData);
    }

    [Theory]
    [InlineData("Verify that it's you")]
    [InlineData("Sign in again")]
    public void Other_account_problem_labels_are_detected(string label)
    {
        var scan = Scan(("Profile 2", "Lee", "lee@example.com", 0, true));
        Assert.Contains(Run(scan, new() { [@"C:\chrome\Profile 2"] = label }), c => c.Status == CheckStatus.Warning && c.Group == HealthChecker.Accounts);
    }

    [Theory]
    [InlineData("Lee")]
    [InlineData("Hi, Lee")]
    [InlineData("Terror")] // a name that merely contains a problem word
    public void Normal_labels_are_not_problems(string label)
    {
        var scan = Scan(("Profile 2", "Lee", "lee@example.com", 0, true));
        Assert.DoesNotContain(Run(scan, new() { [@"C:\chrome\Profile 2"] = label }), c => c.Status == CheckStatus.Warning);
    }

    [Fact]
    public void Signed_out_profiles_are_listed_once()
    {
        var scan = Scan(("Profile 1", "Shop", null, 1, false), ("Profile 2", "Test", null, 1, false), ("Default", "Me", "me@example.com", 1, false));
        var check = Assert.Single(Run(scan), c => c.Title.Contains("signed in"));
        Assert.Equal("2 profiles aren't signed in to an account", check.Title);
        Assert.Contains("Shop, Test", check.Detail);
    }

    [Fact]
    public void Profiles_unused_for_three_months_are_listed_but_open_ones_are_not()
    {
        var scan = Scan(("Profile 1", "Old", "a@example.com", 200, false), ("Profile 2", "OldButOpen", "b@example.com", 200, true), ("Default", "Fresh", "c@example.com", 3, false));
        var check = Assert.Single(Run(scan), c => c.Group == HealthChecker.Profiles);
        Assert.Equal("1 profile wasn't used in the last 3 months", check.Title);
        Assert.Contains("Old", check.Detail);
        Assert.DoesNotContain("OldButOpen", check.Detail);
        Assert.Equal(FixKind.ShowProfile, check.Fix);
    }

    [Fact]
    public void Policies_are_summarised_per_browser()
    {
        var scan = Scan(("Default", "Me", "me@example.com", 1, false));
        var policies = new[]
        {
            new PolicyValue("Google Chrome", RegistryScope.Machine, @"SOFTWARE\Policies\Google\Chrome", "ExtensionSettings", "{}"),
            new PolicyValue("Google Chrome", RegistryScope.Machine, @"SOFTWARE\Policies\Google\Chrome", "BrowserSignin", "1"),
        };
        var check = Assert.Single(Run(scan, policies: policies), c => c.Group == HealthChecker.Policies);
        Assert.Equal("Google Chrome: 2 policies set", check.Title);
        Assert.Equal(FixKind.ShowPolicies, check.Fix);
    }

    [Fact]
    public void Claude_checks_appear_only_when_claude_is_installed()
    {
        var scan = Scan(("Default", "Me", "me@example.com", 1, true));
        Assert.DoesNotContain(Run(scan), c => c.Group == HealthChecker.Claude);

        var profile = scan.AllProfiles.First();
        profile.Extensions.Add(new ExtensionInfo { Id = "fcoeoabgfenejglbffodgkkbkcdhcgfn", Name = "Claude", Enabled = true, Profile = profile });
        Assert.Contains(Run(scan), c => c.Group == HealthChecker.Claude);
    }

    [Fact]
    public void Checks_come_in_page_order()
    {
        var scan = Scan(("Default", "Me", null, 200, false));
        var groups = Run(scan).Select(c => c.Group).Distinct().ToList();
        Assert.Equal(groups.OrderBy(g => HealthChecker.GroupOrder.ToList().IndexOf(g)), groups);
    }
}
