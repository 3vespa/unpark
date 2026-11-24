using Unpark.Core.Debloat;

namespace Unpark.Tests;

public sealed class FakeAppx : IAppxApi
{
    public List<AppxPackageInfo> Packages { get; } = [];
    public HashSet<string> FailOn { get; } = [];
    public List<string> Removed { get; } = [];

    public IReadOnlyList<AppxPackageInfo> ListForCurrentUser() => Packages.ToList();

    public void Remove(string fullName)
    {
        if (FailOn.Contains(fullName)) throw new InvalidOperationException("0x80073CFA : retrait refusé (simulé)");
        Removed.Add(fullName);
        Packages.RemoveAll(p => p.FullName == fullName);
    }

    public FakeAppx Add(string name, string display = "", bool framework = false, bool system = false)
    {
        Packages.Add(new AppxPackageInfo(name, name + "_8wekyb3d8bbwe", name + "_1.0.0.0_x64__8wekyb3d8bbwe", display, framework, false, system));
        return this;
    }
}

public class DebloatTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FakeAppx _appx = new();
    private readonly MemoryLog _log = new();
    private DebloatService Service() => new(_appx, _log, Path.Combine(_dir.Path, "debloat.json"));
    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Jamais_proposees_Store_Calculatrice_Photos_Terminal_Xbox_systeme()
    {
        _appx.Add("Microsoft.WindowsStore").Add("Microsoft.WindowsCalculator").Add("Microsoft.Windows.Photos")
             .Add("Microsoft.WindowsTerminal").Add("Microsoft.XboxIdentityProvider").Add("Microsoft.GamingApp")
             .Add("Microsoft.SecHealthUI").Add("NVIDIACorp.NVIDIAControlPanel").Add("Microsoft.VCLibs.140.00", framework: true)
             .Add("Microsoft.AAD.BrokerPlugin", system: true).Add("MicrosoftWindows.Client.CBS");
        Assert.Empty(Service().Candidates());
    }

    [Fact]
    public void Liste_sure_cochee_le_reste_decoche()
    {
        _appx.Add("Microsoft.BingNews", "Microsoft News").Add("Clipchamp.Clipchamp").Add("king.com.CandyCrushSaga")
             .Add("Microsoft.XboxGamingOverlay").Add("SpotifyAB.SpotifyMusic").Add("Contoso.MonAppli", "Mon appli");
        var c = Service().Candidates();
        Assert.True(c.Single(x => x.Package.Name == "Microsoft.BingNews").DefaultChecked);
        Assert.Equal("Actualités", c.Single(x => x.Package.Name == "Microsoft.BingNews").DisplayName);
        Assert.True(c.Single(x => x.Package.Name == "king.com.CandyCrushSaga").DefaultChecked);
        Assert.False(c.Single(x => x.Package.Name == "Microsoft.XboxGamingOverlay").DefaultChecked);  // Game Bar décochée
        Assert.False(c.Single(x => x.Package.Name == "SpotifyAB.SpotifyMusic").DefaultChecked);
        Assert.Equal("Mon appli", c.Single(x => x.Package.Name == "Contoso.MonAppli").DisplayName);
        Assert.False(c.Single(x => x.Package.Name == "Contoso.MonAppli").DefaultChecked);
        Assert.Equal(AppPolicy.Recommended, c[0].Policy);   // recommandées en premier
    }

    [Fact]
    public void Retrait_avec_historique_et_lien_Store_echec_sans_planter()
    {
        _appx.Add("Microsoft.BingNews").Add("Microsoft.BingWeather");
        _appx.FailOn.Add("Microsoft.BingWeather_1.0.0.0_x64__8wekyb3d8bbwe");
        var results = Service().Remove(Service().Candidates());
        Assert.Single(results, r => r.Success);
        Assert.Contains("refusé", results.Single(r => !r.Success).Error);

        var history = Service().History();
        Assert.Single(history);
        Assert.Equal("ms-windows-store://pdp/?PFN=Microsoft.BingNews_8wekyb3d8bbwe", history[0].StoreUrl);
    }

    [Fact]
    public void Une_appli_protegee_n_est_jamais_retiree_meme_si_demandee()
    {
        _appx.Add("Microsoft.WindowsStore");
        var forced = new DebloatCandidate(_appx.Packages[0], "Store", AppPolicy.Recommended);
        var r = Service().Remove([forced]).Single();
        Assert.False(r.Success);
        Assert.Empty(_appx.Removed);
    }
}
