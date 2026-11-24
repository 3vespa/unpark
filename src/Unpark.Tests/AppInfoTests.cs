using System.Text.RegularExpressions;
using Unpark.Core;

namespace Unpark.Tests;

public class AppInfoTests
{
    [Fact]
    public void Version_est_au_format_semver_sans_suffixe()
    {
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+$"), AppInfo.Version);
        Assert.Equal($"v{AppInfo.Version}", AppInfo.DisplayVersion);
    }

    [Fact]
    public void Nom_et_sous_titre()
    {
        Assert.Equal("Unpark", AppInfo.Name);
        Assert.Equal("Optimisation PC gaming", AppInfo.Subtitle);
    }
}
