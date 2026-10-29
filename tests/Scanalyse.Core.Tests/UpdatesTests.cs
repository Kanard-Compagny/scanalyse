using Scanalyse.Core.Updates;

namespace Scanalyse.Core.Tests;

public class UpdatesTests
{
    [Fact]
    public void Versions_comparees_dans_le_bon_ordre()
    {
        var dev = AppVersion.Parse("0.10.1-dev.35")!;
        var rel = AppVersion.Parse("v0.10.1")!;
        var next = AppVersion.Parse("0.11.0")!;
        Assert.True(rel.IsNewerThan(dev));
        Assert.True(next.IsNewerThan(rel));
        Assert.False(rel.IsNewerThan(AppVersion.Parse("0.10.1+abcdef")));
        Assert.True(AppVersion.Parse("0.10.1-dev.36")!.IsNewerThan(dev));
        Assert.True(AppVersion.Parse("0.10.10")!.IsNewerThan(AppVersion.Parse("0.10.9")));
        Assert.Null(AppVersion.Parse("abc"));
        Assert.Equal("0.10.1-dev.35", dev.ToString());
    }

    [Fact]
    public void Reponse_github_lue()
    {
        const string json = """{"tag_name":"v0.11.0","draft":false,"body":"## Nouveautés","assets":[{"name":"autre.zip","browser_download_url":"https://x/autre.zip"},{"name":"Scanalyse.exe","browser_download_url":"https://github.com/o/r/releases/download/v0.11.0/Scanalyse.exe"}]}""";
        var u = ReleaseFeed.ParseGitHubLatest(json)!;
        Assert.Equal("0.11.0", u.Version.ToString());
        Assert.True(u.DownloadUrl.EndsWith("Scanalyse.exe", StringComparison.Ordinal));
        Assert.Equal("## Nouveautés", u.Notes);
        Assert.Null(ReleaseFeed.ParseGitHubLatest("""{"message":"Not Found"}"""));
        Assert.Null(ReleaseFeed.ParseGitHubLatest("""{"tag_name":"v1.0.0","assets":[]}"""));
    }

    [Fact]
    public void Empreinte_sha256_publiee_avec_la_version()
    {
        const string json = """{"tag_name":"v0.12.0","assets":[{"name":"Scanalyse.exe","browser_download_url":"https://x/Scanalyse.exe"},{"name":"Scanalyse.exe.sha256","browser_download_url":"https://x/Scanalyse.exe.sha256"}]}""";
        var u = ReleaseFeed.ParseGitHubLatest(json)!;
        Assert.Equal("https://x/Scanalyse.exe.sha256", u.ChecksumUrl);
        Assert.Null(ReleaseFeed.ParseGitHubLatest("""{"tag_name":"v0.11.2","assets":[{"name":"Scanalyse.exe","browser_download_url":"https://x/e"}]}""")!.ChecksumUrl);
    }

    [Fact]
    public void Lecture_du_fichier_sha256()
    {
        // Empreinte connue de « abc » (FIPS 180-2).
        const string abc = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
        Assert.Equal(abc, ReleaseFeed.Sha256("abc"u8));
        Assert.Equal(abc, ReleaseFeed.ParseSha256(abc.ToUpperInvariant() + "  Scanalyse.exe\r\n"));
        Assert.Equal(abc, ReleaseFeed.ParseSha256(abc + " *Scanalyse.exe"));
        Assert.Null(ReleaseFeed.ParseSha256("pas une empreinte"));
        Assert.Null(ReleaseFeed.ParseSha256(null));
    }
}
