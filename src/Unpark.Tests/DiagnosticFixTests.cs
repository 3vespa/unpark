using Unpark.Core.Backup;
using Unpark.Core.Diagnostics;
using Unpark.Core.Display;
using Unpark.Core.Settings;

namespace Unpark.Tests;

/// <summary>Faux écrans : fréquence actuelle modifiable, modes disponibles jusqu'à MaxHz.</summary>
public sealed class FakeDisplays : IDisplayModeApi
{
    public List<DisplayData> List { get; } = [];
    public List<(string Device, int Hz)> Changes { get; } = [];
    public bool Fail { get; set; }

    public IReadOnlyList<DisplayData> Displays() => List.ToList();

    public void SetRefreshRate(string deviceName, int width, int height, int hz)
    {
        if (Fail) throw new InvalidOperationException("Windows refuse cette fréquence (simulé).");
        var i = List.FindIndex(d => d.DeviceName == deviceName);
        if (i < 0) throw new InvalidOperationException("Écran introuvable");
        if (List[i].Width != width || List[i].Height != height) throw new InvalidOperationException("Résolution changée");
        List[i] = List[i] with { CurrentHz = hz };
        Changes.Add((deviceName, hz));
    }

    public int Hz(string device) => List.Single(d => d.DeviceName == device).CurrentHz;
}

public class RefreshRateTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FakeDisplays _displays = new();
    private readonly SettingContext _ctx;
    private readonly RefreshRateChanger _changer;
    private const string D1 = @"\\.\DISPLAY1";
    private const string D2 = @"\\.\DISPLAY2";

    public RefreshRateTests()
    {
        var log = new MemoryLog();
        _ctx = new SettingContext(new FakeRegistry(), new BackupStore(Path.Combine(_dir.Path, "backup.json"), log), log);
        _changer = new RefreshRateChanger(_ctx, _displays);
        _displays.List.Add(new DisplayData("Écran 1", 2560, 1440, 60, 165, D1));
        _displays.List.Add(new DisplayData("Écran 2", 1920, 1080, 60, 144, D2));
    }

    public void Dispose() => _dir.Dispose();

    private SettingsEngine Engine() => new(_ctx, [], () => DateTime.MinValue, _changer.Resolve);

    [Fact]
    public void Garder_la_frequence_puis_Tout_annuler_remet_l_ancienne()
    {
        var trial = _changer.Start(D1, "Écran 1", 165);
        Assert.Equal(165, _displays.Hz(D1));
        _changer.Keep(trial);
        Assert.Equal("Fréquence de l'écran (Écran 1)", _ctx.Backup.Entries.Single().SettingName);

        var results = Engine().RevertAll();
        Assert.All(results, r => Assert.True(r.Success));
        Assert.Equal(60, _displays.Hz(D1));
        Assert.Empty(_ctx.Backup.Entries);
    }

    [Fact]
    public void Sans_reponse_retour_a_l_ancienne_frequence_et_rien_a_annuler()
    {
        var trial = _changer.Start(D1, "Écran 1", 165);
        _changer.Undo(trial, "pas de réponse en 15 s");
        Assert.Equal(60, _displays.Hz(D1));
        Assert.Empty(_ctx.Backup.Entries);
    }

    [Fact]
    public void Plusieurs_ecrans_independants()
    {
        _changer.Keep(_changer.Start(D1, "Écran 1", 165));
        _changer.Keep(_changer.Start(D2, "Écran 2", 144));
        Assert.Equal(2, _ctx.Backup.Entries.Count);
        Engine().RevertAll();
        Assert.Equal(60, _displays.Hz(D1));
        Assert.Equal(60, _displays.Hz(D2));
    }

    [Fact]
    public void Deuxieme_essai_refuse_revient_a_la_frequence_gardee_et_Tout_annuler_a_celle_d_avant_Unpark()
    {
        _displays.List[0] = _displays.List[0] with { MaxHz = 240 };
        _changer.Keep(_changer.Start(D1, "Écran 1", 165));
        var second = _changer.Start(D1, "Écran 1", 240);
        _changer.Undo(second, "pas de réponse");
        Assert.Equal(165, _displays.Hz(D1));        // la fréquence gardée juste avant
        Assert.Single(_ctx.Backup.Entries);          // la sauvegarde d'avant Unpark reste
        Engine().RevertAll();
        Assert.Equal(60, _displays.Hz(D1));          // l'état d'origine exact
    }

    [Fact]
    public void Echec_de_Windows_rien_n_est_modifie_ni_sauvegarde()
    {
        _displays.Fail = true;
        Assert.Throws<InvalidOperationException>(() => _changer.Start(D1, "Écran 1", 165));
        Assert.Equal(60, _displays.Hz(D1));
        Assert.Empty(_ctx.Backup.Entries);
    }

    [Fact]
    public void Ecran_debranche_ou_frequence_impossible_refuses()
    {
        Assert.Throws<InvalidOperationException>(() => _changer.Start(@"\\.\DISPLAY9", "Écran 9", 144));
        Assert.Throws<InvalidOperationException>(() => _changer.Start(D2, "Écran 2", 240));
        Assert.Empty(_displays.Changes);
    }

    [Fact]
    public void Tout_annuler_ecran_debranche_ou_resolution_changee_ne_force_rien()
    {
        _changer.Keep(_changer.Start(D1, "Écran 1", 165));
        _changer.Keep(_changer.Start(D2, "Écran 2", 144));
        _displays.List.RemoveAt(0);                                              // Écran 1 débranché
        _displays.List[0] = _displays.List[0] with { Width = 1280, Height = 720 }; // Écran 2 : autre résolution
        var changesBefore = _displays.Changes.Count;
        var results = Engine().RevertAll();
        Assert.All(results, r => Assert.True(r.Success));
        Assert.Equal(changesBefore, _displays.Changes.Count);
        Assert.Empty(_ctx.Backup.Entries);
    }

    [Fact]
    public void Tout_annuler_echec_de_Windows_sauvegarde_gardee()
    {
        _changer.Keep(_changer.Start(D1, "Écran 1", 165));
        _displays.Fail = true;
        var results = Engine().RevertAll();
        Assert.False(results.Single().Success);
        Assert.Single(_ctx.Backup.Entries); // on pourra réessayer
    }
}

public class DiagnosticActionTests
{
    [Fact]
    public void Ecran_sous_sa_frequence_bouton_Passer_a_X_Hz()
    {
        var check = DiagnosticAnalyzer.Displays([new DisplayData("Écran 1", 2560, 1440, 60, 165, @"\\.\DISPLAY1")]).Single();
        Assert.Equal(DiagStatus.Red, check.Status);
        var action = Assert.IsType<RefreshRateAction>(check.Action);
        Assert.Equal(165, action.TargetHz);
        Assert.Equal(60, action.CurrentHz);
        Assert.Contains("15 secondes", check.Explanation);
    }

    [Fact]
    public void Ecran_a_sa_frequence_max_ou_59_60_pas_de_bouton()
    {
        Assert.Null(DiagnosticAnalyzer.Displays([new DisplayData("Écran 1", 1920, 1080, 144, 144, @"\\.\DISPLAY1")]).Single().Action);
        Assert.Null(DiagnosticAnalyzer.Displays([new DisplayData("Écran 1", 1920, 1080, 59, 60, @"\\.\DISPLAY1")]).Single().Action);
    }

    [Fact]
    public void Plusieurs_ecrans_un_bouton_par_ecran_concerne()
    {
        var checks = DiagnosticAnalyzer.Displays(
        [
            new DisplayData("Écran 1", 2560, 1440, 60, 165, @"\\.\DISPLAY1"),
            new DisplayData("Écran 2", 1920, 1080, 60, 60, @"\\.\DISPLAY2"),
            new DisplayData("Écran 3", 1920, 1080, 60, 144, @"\\.\DISPLAY3"),
        ]);
        Assert.Equal([@"\\.\DISPLAY1", @"\\.\DISPLAY3"], checks.Select(c => c.Action).OfType<RefreshRateAction>().Select(a => a.DeviceName));
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 3070", "NVIDIA", DriverPages.Nvidia)]
    [InlineData("AMD Radeon RX 7800 XT", "AMD", DriverPages.Amd)]
    [InlineData("AMD Radeon(TM) Graphics", "AMD", DriverPages.Amd)]
    [InlineData("Intel(R) UHD Graphics 770", "Intel", DriverPages.Intel)]
    [InlineData("Intel(R) Arc(TM) A770 Graphics", "Intel", DriverPages.Intel)]
    public void Pilote_ancien_bouton_vers_la_page_officielle(string gpu, string vendor, string url)
    {
        var now = new DateTime(2026, 11, 26);
        var check = DiagnosticAnalyzer.Gpus([new GpuData(gpu, "1.0", now.AddMonths(-14), null, false)], null, now).First();
        var action = Assert.IsType<DriverPageAction>(check.Action);
        Assert.Equal(vendor, action.Vendor);
        Assert.Equal(url, action.Url);
        Assert.StartsWith("https://", action.Url);
        Assert.Contains("Unpark n'installe rien", check.Explanation);
    }

    [Fact]
    public void Pilote_recent_ou_carte_inconnue_pas_de_bouton()
    {
        var now = new DateTime(2026, 11, 26);
        Assert.Null(DiagnosticAnalyzer.Gpus([new GpuData("NVIDIA GeForce RTX 4070", "1.0", now.AddMonths(-1), null, false)], null, now).First().Action);
        Assert.Null(DiagnosticAnalyzer.Gpus([new GpuData("Matrox G200", "1.0", now.AddMonths(-30), null, false)], null, now).First().Action);
    }

    [Fact]
    public void Pilote_de_plus_de_6_mois_bouton_aussi()
    {
        var now = new DateTime(2026, 11, 26);
        var check = DiagnosticAnalyzer.Gpus([new GpuData("NVIDIA GeForce RTX 4070", "1.0", now.AddMonths(-8), null, false)], null, now).First();
        Assert.Equal(DiagStatus.Orange, check.Status);
        Assert.IsType<DriverPageAction>(check.Action);
    }

    private static RamData Ram(int configured, int rated, int smbios = 26) =>
        new([new RamModule(16L << 30, rated, configured, smbios, "DIMM_A2", "ChannelA", ""), new RamModule(16L << 30, rated, configured, smbios, "DIMM_B2", "ChannelB", "")],
            32L << 30, 16L << 30);

    [Fact]
    public void XMP_desactive_guide_sur_la_ligne()
    {
        var board = new BoardData("ASUSTeK COMPUTER INC.", "ROG STRIX B550-F GAMING", "System manufacturer", "System Product Name", "AuthenticAMD");
        var check = DiagnosticAnalyzer.Ram(Ram(2133, 3600), board).Single(c => c.Title.StartsWith("Vitesse"));
        var guide = Assert.IsType<BiosGuideAction>(check.Action).Guide;
        Assert.Equal("D.O.C.P.", guide.ProfileName);
        Assert.Equal("ASUS", guide.Brand);
        Assert.True(guide.Available);
    }

    [Fact]
    public void XMP_actif_pas_de_guide() =>
        Assert.Null(DiagnosticAnalyzer.Ram(Ram(3600, 3600)).Single(c => c.Title.StartsWith("Vitesse")).Action);
}

public class BiosGuideTests
{
    private static BoardData Board(string board, string cpu = "GenuineIntel", string system = "System manufacturer") =>
        new(board, "X", system, "Y", cpu);

    [Theory]
    [InlineData("ASUSTeK COMPUTER INC.", BoardBrand.Asus, "ASUS")]
    [InlineData("Micro-Star International Co., Ltd.", BoardBrand.Msi, "MSI")]
    [InlineData("Gigabyte Technology Co., Ltd.", BoardBrand.Gigabyte, "Gigabyte")]
    [InlineData("ASRock", BoardBrand.AsRock, "ASRock")]
    [InlineData("Dell Inc.", BoardBrand.Oem, "Dell")]
    [InlineData("HP", BoardBrand.Oem, "HP")]
    [InlineData("Hewlett-Packard", BoardBrand.Oem, "HP")]
    [InlineData("LENOVO", BoardBrand.Oem, "Lenovo")]
    [InlineData("To Be Filled By O.E.M.", BoardBrand.Unknown, "")]
    public void Marque_de_la_carte_mere(string manufacturer, BoardBrand brand, string name) =>
        Assert.Equal((brand, name), BiosGuides.DetectBrand(Board(manufacturer)));

    [Fact]
    public void Mots_contenant_HP_ne_sont_pas_HP() =>
        Assert.Equal(BoardBrand.Unknown, BiosGuides.DetectBrand(Board("SHPX Corp")).Brand);

    [Theory]
    [InlineData(BoardBrand.Asus, false, "DDR4", "XMP")]
    [InlineData(BoardBrand.Asus, true, "DDR4", "D.O.C.P.")]
    [InlineData(BoardBrand.Msi, true, "DDR4", "A-XMP")]
    [InlineData(BoardBrand.Gigabyte, true, "DDR4", "XMP")]
    [InlineData(BoardBrand.AsRock, true, "DDR5", "EXPO")]
    [InlineData(BoardBrand.Msi, false, "DDR5", "XMP")]
    [InlineData(BoardBrand.Unknown, true, "DDR5", "EXPO")]
    public void Nom_du_reglage_selon_la_marque(BoardBrand brand, bool amd, string ddr, string expected) =>
        Assert.Equal(expected, BiosGuides.ProfileName(brand, amd, ddr));

    [Fact]
    public void Guide_avec_touche_chemin_et_avertissement()
    {
        var g = BiosGuides.For(Board("Micro-Star International Co., Ltd.", "AuthenticAMD"), "DDR5", isLaptop: false);
        Assert.Equal("Activer EXPO sur ta carte mère MSI", g.Title);
        Assert.Contains(g.Steps, s => s.Contains("Suppr"));
        Assert.Contains(g.Steps, s => s.Contains("EXPO"));
        Assert.Contains("désactive", g.Note);
    }

    [Fact]
    public void PC_de_marque_reglage_souvent_indisponible()
    {
        var g = BiosGuides.For(Board("Dell Inc.", system: "Dell Inc."), "DDR4", isLaptop: false);
        Assert.False(g.Available);
        Assert.Contains("souvent pas", g.Steps[0]);
        Assert.Contains(g.Steps, s => s.Contains("F2"));
    }

    [Fact]
    public void Portable_reglage_presque_jamais_disponible()
    {
        var g = BiosGuides.For(Board("ASUSTeK COMPUTER INC."), "DDR5", isLaptop: true);
        Assert.False(g.Available);
        Assert.Contains("portable", g.Steps[0]);
    }

    [Fact]
    public void Marque_inconnue_guide_general()
    {
        var g = BiosGuides.For(null, "DDR4", isLaptop: false);
        Assert.True(g.Available);
        Assert.Equal("Activer XMP dans le BIOS", g.Title);
        Assert.Contains(g.Steps, s => s.Contains("DOCP"));
    }
}
