using BPM.Core.Analysis;
using BPM.Core.Chromium;
using BPM.Core.Models;

namespace BPM.Core.Tests;

public sealed class AnalysisTests
{
    [Theory]
    [InlineData("<all_urls>", true)]
    [InlineData("*://*/*", true)]
    [InlineData("https://*/*", true)]
    [InlineData("http://*/*", true)]
    [InlineData("https://example.com/*", false)]
    [InlineData("https://*.example.com/*", false)]
    [InlineData("file:///*", false)]
    [InlineData("", false)]
    public void IsAllSites_recognises_patterns_that_cover_every_website(string pattern, bool expected)
    {
        Assert.Equal(expected, PermissionCatalog.IsAllSites(pattern));
    }

    [Fact]
    public void DescribeHosts_with_no_patterns_means_no_access()
    {
        var result = PermissionCatalog.DescribeHosts([]);
        Assert.Equal("No access to websites", result.Description);
        Assert.Equal(PermissionWeight.Info, result.Weight);
    }

    [Fact]
    public void DescribeHosts_with_all_sites_is_high_weight()
    {
        var result = PermissionCatalog.DescribeHosts(["https://example.com/*", "<all_urls>"]);
        Assert.Equal("Read and change all your data on all websites", result.Description);
        Assert.Equal(PermissionWeight.High, result.Weight);
    }

    [Fact]
    public void DescribeHosts_names_a_single_site()
    {
        var result = PermissionCatalog.DescribeHosts(["https://*.example.com/*"]);
        Assert.Equal("Read and change your data on 1 site: example.com", result.Description);
        Assert.Equal(PermissionWeight.Medium, result.Weight);
    }

    [Fact]
    public void DescribeHosts_lists_three_sites_and_counts_the_rest()
    {
        var result = PermissionCatalog.DescribeHosts(Enumerable.Range(1, 5).Select(i => $"https://site{i}.example.com/*").ToList());
        Assert.StartsWith("Read and change your data on 5 sites: site1.example.com, site2.example.com, site3.example.com", result.Description);
        Assert.EndsWith(" and 2 more", result.Description);
    }

    [Fact]
    public void DescribeHosts_with_only_file_access_mentions_local_files()
    {
        Assert.Equal("Read your local files", PermissionCatalog.DescribeHosts(["file:///*"]).Description);
    }

    [Fact]
    public void Storage_only_extension_is_low_risk()
    {
        var risk = RiskAnalyzer.Assess(TestFixtures.Extension(api: ["storage"]));
        Assert.Equal(RiskLevel.Low, risk.Level);
        Assert.Empty(risk.Reasons);
    }

    [Fact]
    public void All_sites_plus_cookies_is_high_risk_with_a_cookie_warning()
    {
        var risk = RiskAnalyzer.Assess(TestFixtures.Extension(api: ["cookies"], hosts: ["<all_urls>"]));
        Assert.Equal(RiskLevel.High, risk.Level);
        Assert.Contains("Can read your sign-in cookies on every website", risk.Reasons);
    }

    [Fact]
    public void Developer_extension_is_at_least_medium_risk()
    {
        var risk = RiskAnalyzer.Assess(TestFixtures.Extension(ExtensionSource.Developer, storeUrl: null));
        Assert.True(risk.Level >= RiskLevel.Medium);
    }

    [Fact]
    public void ReadBits_accepts_a_number_a_list_or_nothing()
    {
        Assert.Equal(1 | 1 << 16, DisableReasonCodes.ReadBits(System.Text.Json.Nodes.JsonNode.Parse("65537")));
        Assert.Equal(1 | 1 << 13, DisableReasonCodes.ReadBits(System.Text.Json.Nodes.JsonNode.Parse("[1, 8192]")));
        Assert.Equal(0, DisableReasonCodes.ReadBits(null));
    }

    [Fact]
    public void Describe_turns_bits_into_plain_english()
    {
        Assert.Equal(["turned off by you", "blocked by policy"], DisableReasonCodes.Describe(1 | 1 << 16));
    }
}
