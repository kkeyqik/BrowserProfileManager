using System.Text.Json.Nodes;
using BPM.Core.Actions;
using BPM.Core.Automation;
using BPM.Core.Models;

namespace BPM.Core.Tests;

public class ExtensionSettingsPolicyTests
{
    static void AssertJsonEqual(string expected, string? actual)
    {
        Assert.NotNull(actual);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(actual!)), $"Expected {expected} but was {actual}");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blocking_with_no_existing_policy_creates_a_single_entry(string? existing)
    {
        var json = ExtensionSettingsPolicy.Apply(existing, "abc", ExtensionSettingsPolicy.Blocked);

        AssertJsonEqual("""{"abc":{"installation_mode":"blocked"}}""", json);
    }

    [Fact]
    public void Setting_a_mode_keeps_other_extensions_and_other_keys_of_the_same_entry()
    {
        var existing = """{"other":{"installation_mode":"allowed"},"abc":{"runtime_blocked_hosts":["*://*.example.com"]}}""";

        var json = ExtensionSettingsPolicy.Apply(existing, "abc", "blocked");

        AssertJsonEqual("""
            {"other":{"installation_mode":"allowed"},
             "abc":{"runtime_blocked_hosts":["*://*.example.com"],"installation_mode":"blocked"}}
            """, json);
    }

    [Fact]
    public void Switching_blocked_to_removed_replaces_the_mode()
    {
        var existing = """{"abc":{"installation_mode":"blocked"}}""";

        var json = ExtensionSettingsPolicy.Apply(existing, "abc", ExtensionSettingsPolicy.Removed);

        AssertJsonEqual("""{"abc":{"installation_mode":"removed"}}""", json);
    }

    [Fact]
    public void Clearing_removes_only_installation_mode_and_keeps_other_keys()
    {
        var existing = """{"abc":{"installation_mode":"blocked","runtime_blocked_hosts":["x"]},"other":{"installation_mode":"blocked"}}""";

        var json = ExtensionSettingsPolicy.Apply(existing, "abc", null);

        AssertJsonEqual("""{"abc":{"runtime_blocked_hosts":["x"]},"other":{"installation_mode":"blocked"}}""", json);
    }

    [Fact]
    public void Clearing_removes_the_entry_when_it_becomes_empty()
    {
        var existing = """{"abc":{"installation_mode":"blocked"},"other":{"installation_mode":"blocked"}}""";

        var json = ExtensionSettingsPolicy.Apply(existing, "abc", null);

        AssertJsonEqual("""{"other":{"installation_mode":"blocked"}}""", json);
    }

    [Fact]
    public void Clearing_the_last_entry_returns_null_so_the_value_can_be_deleted()
    {
        var json = ExtensionSettingsPolicy.Apply("""{"abc":{"installation_mode":"blocked"}}""", "abc", null);

        Assert.Null(json);
    }

    [Fact]
    public void Clearing_an_unknown_extension_leaves_the_policy_unchanged()
    {
        var json = ExtensionSettingsPolicy.Apply("""{"other":{"installation_mode":"blocked"}}""", "abc", null);

        AssertJsonEqual("""{"other":{"installation_mode":"blocked"}}""", json);
    }

    [Fact]
    public void Clearing_with_no_existing_policy_returns_null()
    {
        Assert.Null(ExtensionSettingsPolicy.Apply(null, "abc", null));
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("[1,2,3]")]
    [InlineData("\"text\"")]
    public void Malformed_existing_json_is_refused_rather_than_overwritten(string existing)
    {
        Assert.Throws<InvalidDataException>(() => ExtensionSettingsPolicy.Apply(existing, "abc", "blocked"));
    }

    [Theory]
    [InlineData("force_installed")]
    [InlineData("allowed")]
    public void Unblock_leaves_an_admins_own_mode_alone(string adminMode)
    {
        var existing = "{\"abc\":{\"installation_mode\":\"" + adminMode + "\"}}";

        var json = ExtensionSettingsPolicy.Apply(existing, "abc", null);

        AssertJsonEqual(existing, json);
    }

    [Fact]
    public void ApplyExact_restores_a_previous_mode_and_keeps_other_extensions()
    {
        var current = """{"abc":{"installation_mode":"blocked"},"other":{"installation_mode":"blocked"}}""";

        AssertJsonEqual("""{"abc":{"installation_mode":"allowed"},"other":{"installation_mode":"blocked"}}""",
            ExtensionSettingsPolicy.ApplyExact(current, "abc", "allowed"));
        AssertJsonEqual("""{"other":{"installation_mode":"blocked"}}""",
            ExtensionSettingsPolicy.ApplyExact(current, "abc", null));
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("", null, true)]
    [InlineData("""{"a":{"installation_mode":"blocked"}}""", "{ \"a\" : { \"installation_mode\" : \"blocked\" } }", true)]
    [InlineData("""{"a":{"installation_mode":"blocked"}}""", """{"a":{"installation_mode":"removed"}}""", false)]
    [InlineData("""{"a":{}}""", null, false)]
    public void SameJson_ignores_formatting(string? a, string? b, bool same)
    {
        Assert.Equal(same, ExtensionSettingsPolicy.SameJson(a, b));
    }

    [Fact]
    public void ModeFor_returns_the_extensions_mode()
    {
        var existing = """{"abc":{"installation_mode":"removed"},"other":{"installation_mode":"blocked"}}""";

        Assert.Equal("removed", ExtensionSettingsPolicy.ModeFor(existing, "abc"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{broken")]
    [InlineData("""{"other":{"installation_mode":"blocked"}}""")]
    [InlineData("""{"abc":{"runtime_blocked_hosts":["x"]}}""")]
    public void ModeFor_returns_null_when_no_mode_is_set(string? json)
    {
        Assert.Null(ExtensionSettingsPolicy.ModeFor(json, "abc"));
    }
}

public class ElevatedExecutorTests
{
    const string Pdf = "efaidnbmnnnibpcajpcglclefindmkaj";

    static RegistryOperation Set(string key, string name = "ExtensionSettings") => new(RegistryOpKind.SetString, key, name, "{}");
    static RegistryOperation Delete(string key) => new(RegistryOpKind.DeleteKey, key);

    [Fact]
    public void Writing_a_browsers_ExtensionSettings_policy_is_allowed()
    {
        Assert.True(ElevatedExecutor.IsAllowed(Set(@"SOFTWARE\Policies\Google\Chrome")));
        Assert.True(ElevatedExecutor.IsAllowed(Set(@"SOFTWARE\Policies\Microsoft\Edge")));
        Assert.True(ElevatedExecutor.IsAllowed(new RegistryOperation(RegistryOpKind.DeleteValue, @"SOFTWARE\Policies\BraveSoftware\Brave", "ExtensionSettings")));
    }

    [Theory]
    [InlineData(@"SOFTWARE\Google\Chrome\Extensions\" + Pdf)]
    [InlineData(@"SOFTWARE\Microsoft\Edge\Extensions\" + Pdf)]
    [InlineData(@"SOFTWARE\Google\Chrome\NativeMessagingHosts\com.example.host")]
    public void Removing_or_restoring_one_sideload_or_native_host_entry_is_allowed(string key)
    {
        Assert.True(ElevatedExecutor.IsAllowed(Delete(key)));
        Assert.True(ElevatedExecutor.IsAllowed(new RegistryOperation(RegistryOpKind.CreateKey, key, Values: [])));
    }

    [Theory]
    [InlineData(@"SOFTWARE\Policies\Google\Chrome", "ProxyMode")] // other policies
    [InlineData(@"SOFTWARE\Policies\Google\Chrome\ExtensionInstallForcelist", "1")]
    [InlineData(@"SOFTWARE\Policies\Microsoft\Windows Defender", "ExtensionSettings")]
    [InlineData(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "ExtensionSettings")]
    public void Other_values_and_keys_are_refused(string key, string name)
    {
        Assert.False(ElevatedExecutor.IsAllowed(Set(key, name)));
    }

    [Theory]
    [InlineData(@"SOFTWARE\Policies\Google\Chrome")]          // a whole policy key
    [InlineData(@"SOFTWARE\Policies\Google")]
    [InlineData(@"SOFTWARE\Google\Chrome\Extensions")]         // the parent itself
    [InlineData(@"SOFTWARE\Google\Chrome\Extensions\a\b")]     // nested
    [InlineData(@"SOFTWARE\WOW6432Node\Google\Chrome\Extensions\" + Pdf)] // 32-bit view goes through the flag instead
    [InlineData(@"SOFTWARE\Google\Chrome\Extensions\..")]
    [InlineData(@"SOFTWARE\Google\Chrome\Extensions\x/y")]
    [InlineData(@"SOFTWARE\Google\Chrome\Extensions\" + Pdf + "\0")]
    [InlineData(@"SOFTWARE\Google\Chrome\\Extensions\x")]
    [InlineData(@"\SOFTWARE\Google\Chrome\Extensions\x")]
    [InlineData("")]
    public void Deleting_anything_else_is_refused(string key)
    {
        Assert.False(ElevatedExecutor.IsAllowed(Delete(key)));
    }

    [Fact]
    public void Encoded_request_round_trips_its_operations()
    {
        var request = new ElevatedRequest
        {
            RefreshMachinePolicy = true,
            Operations =
            [
                new RegistryOperation(RegistryOpKind.SetString, @"SOFTWARE\Policies\Google\Chrome", "ExtensionSettings", "{}"),
                new RegistryOperation(RegistryOpKind.CreateKey, @"SOFTWARE\Google\Chrome\Extensions\" + Pdf, Wow64_32: true,
                    Values: new Dictionary<string, string> { ["update_url"] = "https://example.com" }),
                new RegistryOperation(RegistryOpKind.DeleteKey, @"SOFTWARE\Google\Chrome\NativeMessagingHosts\com.example.host"),
            ],
        };
        var decoded = ElevatedExecutor.DecodeRequest(ElevatedExecutor.EncodeRequest(request));

        Assert.True(decoded.RefreshMachinePolicy);
        Assert.Equal(request.Operations.Select(o => (o.Op, o.Key, o.Name, o.Value, o.Wow64_32)),
                     decoded.Operations.Select(o => (o.Op, o.Key, o.Name, o.Value, o.Wow64_32)));
        Assert.Equal("https://example.com", decoded.Operations[1].Values!["update_url"]);
    }

    [Fact]
    public void Garbage_on_the_elevated_command_line_is_rejected_without_touching_the_registry()
    {
        Assert.Equal(ElevatedExecutor.ExitBadRequest, ElevatedExecutor.RunFromCommandLine("not base64!"));
    }

    [Fact]
    public void A_request_with_a_disallowed_operation_is_refused_as_a_whole()
    {
        var request = new ElevatedRequest { Operations = [Delete(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run")] };

        Assert.Equal(ElevatedExecutor.ExitRefused, ElevatedExecutor.RunFromCommandLine(ElevatedExecutor.EncodeRequest(request)));
    }
}

public class ProfileMatchTests
{
    [Theory]
    [InlineData("Your Chrome", "Your Chrome", true)]
    [InlineData("Alex (Your Chrome)", "Your Chrome", true)]
    [InlineData("Person 10", "Person 1", false)]
    [InlineData("Samuel (Sam)", "S", false)]
    [InlineData("Work", "Your Chrome", false)]
    public void Profile_button_must_name_the_exact_profile(string avatar, string profile, bool match)
    {
        var target = new ProfileInfo { Key = "k", Path = "p", Name = profile };
        Assert.Equal(match, BrowserAutomation.NamesProfile(avatar, target, [target]));
    }

    // Profile setups like ones seen on a real Chrome (Oct 2026), with made-up names.
    static readonly ProfileInfo YourChrome = new() { Key = "Default", Path = "d", Name = "Your Chrome", GivenName = "Alex", FullName = "Alex Mehta" };
    static readonly ProfileInfo Alex = new() { Key = "Profile 1", Path = "1", Name = "Alex", GivenName = "Alex", FullName = "Alex Kumar" };
    static readonly ProfileInfo Omar = new() { Key = "Profile 4", Path = "4", Name = "Omar", GivenName = "Omar", FullName = "Omar Haddad" };
    static readonly ProfileInfo[] All = [YourChrome, Alex, Omar];

    [Fact]
    public void A_just_opened_profiles_greeting_is_not_accepted_yet()
    {
        Assert.False(BrowserAutomation.NamesProfile("Hi, Omar", Omar, All));
        Assert.True(BrowserAutomation.NamesProfile("Omar", Omar, All));
    }

    [Fact]
    public void A_first_name_shared_by_two_profiles_only_matches_the_profile_named_that()
    {
        Assert.True(BrowserAutomation.NamesProfile("Alex (Your Chrome)", YourChrome, All));
        Assert.False(BrowserAutomation.NamesProfile("Alex", YourChrome, All)); // that's the "Alex" profile's button
        Assert.True(BrowserAutomation.NamesProfile("Alex", Alex, All));
        Assert.False(BrowserAutomation.NamesProfile("Alex (Your Chrome)", Alex, All));
    }

    [Theory]
    [InlineData("Chrome.UserData.Profile23", "Profile 23", true)]
    [InlineData("Chrome.UserData.Profile23", "Profile 2", false)]
    [InlineData("Chrome.UserData.Profile5", "Default", false)]
    [InlineData("Chrome", "Default", true)]
    [InlineData("Chrome", "Profile 5", false)]
    [InlineData("Some.Other.Form", "Profile 5", null)]
    [InlineData(null, "Profile 5", null)]
    public void Taskbar_id_names_the_profile_folder(string? appId, string key, bool? expected)
    {
        // ID formats seen on a real Chrome (Oct 2026).
        var browser = TestFixtures.BrowserFor("chrome", @"C:\Users\me\AppData\Local\Google\Chrome\User Data");
        var profile = new ProfileInfo { Key = key, Path = "p", Name = "x", Browser = browser };
        Assert.Equal(expected, BrowserAutomation.TaskbarIdNamesProfile(appId, profile));
    }

    [Fact]
    public void An_unshared_account_name_identifies_the_profile()
    {
        var work = new ProfileInfo { Key = "Profile 9", Path = "9", Name = "Work", GivenName = "Priya" };
        Assert.True(BrowserAutomation.NamesProfile("Priya", work, [.. All, work]));
    }
}

public class BrowserLauncherTests
{
    static ProfileInfo ProfileOf(string catalogId, string key, string name, bool running = false, bool open = false) => new()
    {
        Browser = TestFixtures.BrowserFor(catalogId, @"C:\unused", running),
        Key = key, Path = @"C:\unused\" + key, Name = name, IsOpen = open,
    };

    [Fact]
    public void Chromium_profile_uses_profile_directory_argument()
    {
        var args = BrowserLauncher.ProfileArguments(ProfileOf("chrome", "Profile 3", "Work"));

        Assert.Equal(["--profile-directory=Profile 3"], args);
    }

    [Fact]
    public void Chromium_new_window_adds_new_window_argument()
    {
        var args = BrowserLauncher.ProfileArguments(ProfileOf("chrome", "Profile 3", "Work"), newWindow: true);

        Assert.Equal(["--profile-directory=Profile 3", "--new-window"], args);
    }

    [Fact]
    public void Single_profile_chromium_layout_passes_no_profile_argument()
    {
        var args = BrowserLauncher.ProfileArguments(ProfileOf("opera", "", "Opera"));

        Assert.Empty(args);
    }

    [Fact]
    public void Firefox_profile_is_selected_by_name()
    {
        var args = BrowserLauncher.ProfileArguments(ProfileOf("firefox", "Profiles/abc.work", "work"));

        Assert.Equal(["-P", "work"], args);
    }

    [Fact]
    public void Firefox_adds_new_instance_when_browser_runs_but_profile_is_not_open()
    {
        var args = BrowserLauncher.ProfileArguments(ProfileOf("firefox", "Profiles/abc.work", "work", running: true, open: false));

        Assert.Equal(["-P", "work", "--new-instance"], args);
    }

    [Fact]
    public void Firefox_does_not_add_new_instance_when_profile_is_already_open()
    {
        var args = BrowserLauncher.ProfileArguments(ProfileOf("firefox", "Profiles/abc.work", "work", running: true, open: true));

        Assert.Equal(["-P", "work"], args);
    }
}
