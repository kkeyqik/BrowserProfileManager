using BPM.Core.Browsers;

namespace BPM.Core.Tests;

public sealed class DetectorTests
{
    [Fact]
    public void ExeFromCommand_takes_the_quoted_path_and_drops_arguments()
    {
        Assert.Equal(@"C:\Program Files\Some Browser\browser.exe",
            BrowserDetector.ExeFromCommand("\"C:\\Program Files\\Some Browser\\browser.exe\" --single-argument %1"));
    }

    [Fact]
    public void ExeFromCommand_cuts_an_unquoted_path_after_exe()
    {
        Assert.Equal(@"C:\Tools\browser.exe", BrowserDetector.ExeFromCommand(@"C:\Tools\browser.exe -url %1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ExeFromCommand_returns_null_for_empty_input(string? command)
    {
        Assert.Null(BrowserDetector.ExeFromCommand(command));
    }
}
