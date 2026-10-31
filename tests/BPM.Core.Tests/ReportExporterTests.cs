using System.Text;
using System.Text.Json;
using BPM.Core.Analysis;
using BPM.Core.Models;
using BPM.Core.Reports;

namespace BPM.Core.Tests;

public class ReportExporterTests
{
    private const string Evil = "Evil, \"quoted\" <script>";

    private static ExtensionInfo Ext(ProfileInfo p, string id, string name, bool enabled = true, string[]? reasons = null,
        RiskLevel risk = RiskLevel.Low, string[]? riskReasons = null, long bits = 0) => new()
    {
        Id = id, Name = name, Version = "1.0", Enabled = enabled, DisableReasons = reasons ?? [],
        DisableReasonBits = bits, ManifestVersion = 3, InstalledAt = new DateTime(2026, 3, 5),
        Source = ExtensionSource.Store, StoreUrl = "https://example.com/store?a=1&b=2",
        Risk = new RiskAssessment(risk, riskReasons ?? []), Profile = p,
    };

    private static ProfileInfo Profile(BrowserInfo b, string key, string name, string? email, bool open = false)
    {
        var p = new ProfileInfo { Key = key, Path = @"C:\x\" + key, Name = name, Email = email, IsOpen = open, Browser = b };
        b.Profiles.Add(p);
        return p;
    }

    private static ScanResult Scan()
    {
        var scan = new ScanResult { MachineName = "OFFICE-PC", UserName = "alex", ScannedAt = new DateTime(2026, 10, 29, 19, 30, 0) };
        var chrome = TestFixtures.BrowserFor("chrome", @"C:\chrome");
        var edge = TestFixtures.BrowserFor("edge", @"C:\edge");
        scan.Browsers.Add(chrome);
        scan.Browsers.Add(edge);
        var p1 = Profile(chrome, "Default", "Sam", "sam.lee@example.com", open: true);
        var p2 = Profile(chrome, "Profile 1", "work.person@corp.example", "work.person@corp.example");
        Profile(edge, "Default", "Empty", "ab@x.io");
        p1.Extensions.Add(Ext(p1, "aaa", Evil, risk: RiskLevel.High, riskReasons: ["reads all sites", "can see tabs"]));
        p1.Extensions.Add(Ext(p1, "bbb", "Off one", enabled: false, reasons: ["turned off by you", "blocked by policy"]));
        p2.Extensions.Add(Ext(p2, "ccc", "Plain"));
        return scan;
    }

    private static readonly IReadOnlyList<Problem> Problems =
        [new(ProblemSeverity.Warning, "Mail <b>", "Profile sam.lee@example.com has an issue")];

    private static List<List<string>> ParseCsv(string csv)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var sb = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < csv.Length && csv[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else sb.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { row.Add(sb.ToString()); sb.Clear(); }
            else if (c == '\r') { }
            else if (c == '\n') { row.Add(sb.ToString()); sb.Clear(); rows.Add(row); row = []; }
            else sb.Append(c);
        }
        return rows;
    }

    [Fact]
    public void Csv_HasHeaderAndOneRowPerExtensionPlusEmptyProfiles()
    {
        var rows = ParseCsv(ReportExporter.BuildCsv(Scan(), false));
        Assert.Equal(1 + 3 + 1, rows.Count);
        Assert.Equal("Browser", rows[0][0]);
        Assert.All(rows, r => Assert.Equal(13, r.Count));
        Assert.Equal(Evil, rows[1][3]);
        Assert.Equal("High", rows[1][9]);
        Assert.Equal("reads all sites; can see tabs", rows[1][10]);
        Assert.Equal("Off", rows[2][6]);
        Assert.Equal("turned off by you; blocked by policy", rows[2][7]);
        Assert.Equal("Store", rows[2][8]);
        Assert.Equal("2026-03-05", rows[2][12]);
        Assert.Equal("", rows[4][3]);
    }

    [Fact]
    public void Csv_QuotesSpecialFields()
    {
        var csv = ReportExporter.BuildCsv(Scan(), false);
        Assert.Contains("\"Evil, \"\"quoted\"\" <script>\"", csv);
    }

    [Fact]
    public void HideEmails_MasksEverywhere()
    {
        var scan = Scan();
        foreach (var text in new[]
        {
            ReportExporter.BuildCsv(scan, true),
            ReportExporter.BuildJson(scan, Problems, true),
            ReportExporter.BuildHtml(scan, Problems, true),
        })
        {
            Assert.DoesNotContain("sam.lee@example.com", text);
            Assert.DoesNotContain("work.person@corp.example", text);
            Assert.DoesNotContain("ab@x.io", text);
            Assert.Contains("s***@example.com", text);
        }
        Assert.Contains("a***@x.io", ReportExporter.BuildCsv(scan, true));
        Assert.Contains("sam.lee@example.com", ReportExporter.BuildCsv(scan, false));
    }

    [Fact]
    public void Html_EncodesScriptAndHasStructure()
    {
        var html = ReportExporter.BuildHtml(Scan(), Problems, false);
        Assert.DoesNotContain("<script", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("Mail &lt;b&gt;", html);
        Assert.Contains("<title>Browser Profile Manager report</title>", html);
        Assert.Contains("<h3>", html);
        Assert.Contains("badge open", html);
        Assert.Contains("prefers-color-scheme:dark", html);
        Assert.Contains("title=\"reads all sites; can see tabs\"", html);
        Assert.Contains("a=1&amp;b=2", html);
        Assert.Contains("Read-only scan: no cookies, passwords or history were read.", html);
    }

    [Fact]
    public void Json_ParsesAndSummaryMatches()
    {
        using var doc = JsonDocument.Parse(ReportExporter.BuildJson(Scan(), Problems, false));
        var root = doc.RootElement;
        var s = root.GetProperty("summary");
        Assert.Equal(2, s.GetProperty("browsers").GetInt32());
        Assert.Equal(3, s.GetProperty("profiles").GetInt32());
        Assert.Equal(3, s.GetProperty("extensions").GetInt32());
        Assert.Equal(2, s.GetProperty("enabled").GetInt32());
        Assert.Equal(1, s.GetProperty("highRisk").GetInt32());
        Assert.Equal(0, s.GetProperty("waitingForApproval").GetInt32());
        Assert.Equal("warning", root.GetProperty("problems")[0].GetProperty("severity").GetString());
        var ext = root.GetProperty("browsers")[0].GetProperty("profiles")[0].GetProperty("extensions")[0];
        Assert.Equal(Evil, ext.GetProperty("name").GetString());
        Assert.Equal("high", ext.GetProperty("risk").GetString());
        Assert.Equal("store", ext.GetProperty("source").GetString());
        Assert.Equal("OFFICE-PC", root.GetProperty("machine").GetString());
    }

    [Theory]
    [InlineData("sam.lee@example.com", "s***@example.com")]
    [InlineData("ab@x.io", "a***@x.io")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("no-at-sign", "***")]
    public void MaskEmail_Cases(string? input, string? expected) => Assert.Equal(expected, ReportExporter.MaskEmail(input));

    [Fact]
    public void SuggestedFileName_UsesMachineAndTime()
    {
        var scan = Scan();
        Assert.Equal("BrowserProfileReport_OFFICE-PC_20261029-1930.html", ReportExporter.SuggestedFileName(scan, ReportFormat.Html));
        Assert.EndsWith(".csv", ReportExporter.SuggestedFileName(scan, ReportFormat.Csv));
        Assert.EndsWith(".json", ReportExporter.SuggestedFileName(scan, ReportFormat.Json));
    }

    [Theory]
    [InlineData(ReportFormat.Html)]
    [InlineData(ReportFormat.Csv)]
    [InlineData(ReportFormat.Json)]
    public void Export_WritesFile(ReportFormat format)
    {
        using var dir = new TempDir();
        var path = dir.Combine("sub", "report." + format);
        var scan = Scan();
        ReportExporter.Export(scan, Problems, path, format, hideEmails: false);
        var bytes = File.ReadAllBytes(path);
        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        Assert.Equal(format == ReportFormat.Csv, hasBom);
        var text = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
        var expected = format switch
        {
            ReportFormat.Html => ReportExporter.BuildHtml(scan, Problems, false),
            ReportFormat.Csv => ReportExporter.BuildCsv(scan, false),
            _ => ReportExporter.BuildJson(scan, Problems, false),
        };
        Assert.Equal(expected, text);
    }
}
