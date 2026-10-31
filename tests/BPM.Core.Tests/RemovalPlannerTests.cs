using BPM.Core.Actions;
using BPM.Core.Models;

namespace BPM.Core.Tests;

public class RemovalPlannerTests
{
    const string Id = "abcdefghijklmnopabcdefghijklmnop";

    static ExtensionInfo CopyIn(BrowserInfo browser, string profileName, ExtensionSource source = ExtensionSource.Store)
    {
        var profile = new ProfileInfo { Browser = browser, Key = profileName, Path = @"C:\unused\" + profileName, Name = profileName };
        var extension = new ExtensionInfo { Profile = profile, Id = Id, Name = "Thing", Source = source };
        profile.Extensions.Add(extension);
        browser.Profiles.Add(profile);
        return extension;
    }

    static BrowserInfo Browser(string catalogId, bool installed = true) => new()
    {
        Definition = Browsers.BrowserCatalog.Find(catalogId)!,
        ExePath = installed ? @"C:\Program Files\x\browser.exe" : null,
        DataDir = @"C:\unused",
    };

    [Fact]
    public void Every_removable_copy_is_a_target_and_the_rest_are_skipped_with_a_reason()
    {
        var chrome = Browser("chrome");
        var edge = Browser("edge");
        var firefox = Browser("firefox");
        var noExe = Browser("brave", installed: false);
        var scan = new ScanResult();
        scan.Browsers.AddRange([chrome, edge, firefox, noExe]);
        var work = CopyIn(chrome, "Work");
        var home = CopyIn(chrome, "Home");
        var edgeCopy = CopyIn(edge, "Default");
        CopyIn(chrome, "Managed", ExtensionSource.Policy);
        CopyIn(firefox, "default-release");
        CopyIn(noExe, "Default");

        var plan = RemovalPlanner.Plan(scan, Id);

        Assert.Equal([home, work, edgeCopy], plan.Targets); // ordered by browser, then profile
        Assert.Equal(3, plan.Skipped.Count);
        Assert.Contains(plan.Skipped, s => s.Reason.Contains("policy"));
        Assert.Contains(plan.Skipped, s => s.Reason.Contains("about:addons"));
        Assert.Contains(plan.Skipped, s => s.Reason.Contains("wasn't found"));
    }

    [Fact]
    public void Other_extensions_and_a_missing_scan_give_an_empty_plan()
    {
        var chrome = Browser("chrome");
        var scan = new ScanResult();
        scan.Browsers.Add(chrome);
        CopyIn(chrome, "Work");

        Assert.Empty(RemovalPlanner.Plan(scan, "ponmlkjihgfedcbaponmlkjihgfedcba").Targets);
        Assert.Empty(RemovalPlanner.Plan(null, Id).Targets);
    }
}
