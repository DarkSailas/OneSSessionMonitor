using FluentAssertions;
using OneSSessionMonitor.Core.Update;
using Xunit;

namespace OneSSessionMonitor.Tests;

public class GitHubReleaseParserTests
{
    private const string Hash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";
    private const string GuiUrl = "https://github.com/DarkSailas/OneSSessionMonitor/releases/download/v1.6.0/OneSSessionMonitor-Gui-1.6.0-win-x64.zip";

    private static string Release(string tag, string assets, string flags = "") =>
        $$"""{ "tag_name": "{{tag}}", {{flags}} "assets": [ {{assets}} ] }""";

    private static string Asset(string name, string url, long size = 1024, string? digest = null) =>
        $$"""{ "name": "{{name}}", "browser_download_url": "{{url}}", "size": {{size}}{{(digest is null ? "" : $", \"digest\": \"{digest}\"")}} }""";

    private static readonly Version Current = new(1, 5, 0);

    [Fact]
    public void Parse_ShouldReturnGuiAsset_WhenReleaseIsNewer()
    {
        string json = Release("v1.6.0",
            Asset("OneSSessionMonitor-Service-1.6.0-win-x64.zip", "https://github.com/x/y/service.zip") + "," +
            Asset("OneSSessionMonitor-Gui-1.6.0-win-x64.zip", GuiUrl, 2048, "sha256:" + Hash.ToUpperInvariant()));

        var update = GitHubReleaseParser.Parse(json, Current);

        update.Should().NotBeNull();
        update!.Version.Should().Be(new Version(1, 6, 0));
        update.TagName.Should().Be("v1.6.0");
        update.AssetName.Should().Be("OneSSessionMonitor-Gui-1.6.0-win-x64.zip");
        update.DownloadUrl.Should().Be(new Uri(GuiUrl));
        update.SizeBytes.Should().Be(2048);
        update.Sha256.Should().Be(Hash);
    }

    [Theory]
    [InlineData("v1.5.0")]
    [InlineData("v1.4.9")]
    [InlineData("не версия")]
    public void Parse_ShouldReturnNull_WhenReleaseIsNotNewer(string tag)
    {
        GitHubReleaseParser.Parse(Release(tag, Asset("App-Gui.zip", GuiUrl)), Current).Should().BeNull();
    }

    [Fact]
    public void Parse_ShouldTreatFourPartCurrentVersionAsEqual()
    {
        GitHubReleaseParser.Parse(Release("v1.5.0", Asset("App-Gui.zip", GuiUrl)), new Version(1, 5, 0, 0))
            .Should().BeNull();
    }

    [Theory]
    [InlineData("\"draft\": true,")]
    [InlineData("\"prerelease\": true,")]
    public void Parse_ShouldIgnoreDraftsAndPrereleases(string flags)
    {
        GitHubReleaseParser.Parse(Release("v1.6.0", Asset("App-Gui.zip", GuiUrl, 1024, "sha256:" + Hash), flags), Current)
            .Should().BeNull();
    }

    [Theory]
    [InlineData("http://github.com/x/y/App-Gui.zip")]
    [InlineData("https://github.com.evil.example/x/App-Gui.zip")]
    [InlineData("https://evil.example/github.com/App-Gui.zip")]
    [InlineData("https://github.com/other/repo/releases/download/v1.6.0/App-Gui.zip")]
    public void Parse_ShouldRejectUntrustedDownloadUrl(string url)
    {
        GitHubReleaseParser.Parse(Release("v1.6.0", Asset("App-Gui.zip", url, 1024, "sha256:" + Hash)), Current)
            .Should().BeNull();
    }

    [Fact]
    public void Parse_ShouldSkipAssetsThatAreNotGuiZip()
    {
        string json = Release("v1.6.0",
            Asset("OneSSessionMonitor-Service.zip", GuiUrl, 1024, "sha256:" + Hash) + "," +
            Asset("OneSSessionMonitor-Gui.exe", GuiUrl, 1024, "sha256:" + Hash));

        GitHubReleaseParser.Parse(json, Current).Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(GitHubReleaseParser.MaxAssetSizeBytes + 1)]
    public void Parse_ShouldRejectAssetWithBadSize(long size)
    {
        GitHubReleaseParser.Parse(Release("v1.6.0", Asset("App-Gui.zip", GuiUrl, size, "sha256:" + Hash)), Current)
            .Should().BeNull();
    }

    [Fact]
    public void Parse_ShouldRejectAsset_WhenDigestIsMalformed()
    {
        GitHubReleaseParser.Parse(Release("v1.6.0", Asset("App-Gui.zip", GuiUrl, 10, "sha256:xyz")), Current)
            .Should().BeNull();
    }

    [Fact]
    public void Parse_ShouldRejectAsset_WhenDigestIsMissing()
    {
        GitHubReleaseParser.Parse(Release("v1.6.0", Asset("App-Gui.zip", GuiUrl, 10)), Current).Should().BeNull();
    }

    [Theory]
    [InlineData("v2.0.1", 2, 0, 1)]
    [InlineData("1.5", 1, 5, 0)]
    [InlineData("V1.5.2-beta", 1, 5, 2)]
    public void TryParseVersion_ShouldUnderstandTags(string tag, int major, int minor, int build)
    {
        GitHubReleaseParser.TryParseVersion(tag, out var version).Should().BeTrue();
        version.Should().Be(new Version(major, minor, build));
    }
}
