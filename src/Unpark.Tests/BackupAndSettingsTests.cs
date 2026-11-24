using Unpark.Core.Backup;
using Unpark.Core.Registry;
using Unpark.Core.Settings;

namespace Unpark.Tests;

public class BackupAndSettingsTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FakeRegistry _reg = new();
    private readonly MemoryLog _log = new();
    private BackupStore _backup;

    private static readonly RegPath A = new(RegHive.CurrentUser, @"Software\Test", "A");
    private static readonly RegPath B = new(RegHive.LocalMachine, @"SYSTEM\Test", "B");

    public BackupAndSettingsTests() => _backup = NewStore();

    public void Dispose() => _dir.Dispose();

    private string File => Path.Combine(_dir.Path, "backup.json");
    private BackupStore NewStore() => new(File, _log);
    private SettingContext Ctx => new(_reg, _backup, _log);

    private RegistrySetting Setting(string id = "s1", bool restart = false) =>
        new(Ctx, id, "Réglage " + id, "", restart, [new(A, RegValue.DWord(1)), new(B, RegValue.String("on"))]);

    [Fact]
    public void Appliquer_puis_annuler_remet_exactement_l_etat_d_origine()
    {
        _reg.Values[A] = RegValue.DWord(5);          // existait avec une autre valeur
        // B n'existait pas
        var s = Setting();
        Assert.False(s.IsApplied());

        s.Apply();
        Assert.True(s.IsApplied());
        Assert.Equal(RegValue.DWord(1), _reg.Get(A));
        Assert.Equal(RegValue.String("on"), _reg.Get(B));

        s.Revert();
        Assert.Equal(RegValue.DWord(5), _reg.Get(A));
        Assert.Null(_reg.Get(B));                    // supprimée, pas mise à une valeur « par défaut »
        Assert.Null(_backup.Get("s1"));
    }

    [Fact]
    public void Appliquer_deux_fois_garde_l_original_d_avant_Unpark()
    {
        _reg.Values[A] = RegValue.DWord(5);
        var s = Setting();
        s.Apply();
        _reg.Values[A] = RegValue.DWord(9);          // quelqu'un change entre-temps
        s.Apply();                                   // ré-application
        s.Revert();
        Assert.Equal(RegValue.DWord(5), _reg.Get(A));
    }

    [Fact]
    public void La_sauvegarde_est_sur_disque_avant_la_modification_et_survit_au_redemarrage()
    {
        _reg.Values[A] = RegValue.QWord(42);
        Setting().Apply();

        _backup = NewStore();                        // l'appli est relancée
        Setting().Revert();
        Assert.Equal(RegValue.QWord(42), _reg.Get(A));
        Assert.Null(_reg.Get(B));
    }

    [Fact]
    public void Types_de_valeurs_conserves()
    {
        _reg.Values[A] = RegValue.Binary([1, 2, 3]);
        _reg.Values[B] = RegValue.MultiString(["x", "y"]);
        Setting().Apply();
        _backup = NewStore();
        Setting().Revert();
        Assert.True(RegValue.Binary([1, 2, 3]).SameAs(_reg.Get(A)));
        Assert.True(RegValue.MultiString(["x", "y"]).SameAs(_reg.Get(B)));
    }

    [Fact]
    public void Annuler_sans_sauvegarde_ne_touche_a_rien()
    {
        _reg.Values[A] = RegValue.DWord(1);
        Setting().Revert();
        Assert.Equal(RegValue.DWord(1), _reg.Get(A));
        Assert.Contains(_log.Lines, l => l.Contains("rien à annuler"));
    }

    [Fact]
    public void Echec_en_cours_d_application_remet_l_etat_d_origine()
    {
        _reg.Values[A] = RegValue.DWord(5);
        _reg.FailOn = B;
        var engine = new SettingsEngine(Ctx, [Setting()], () => DateTime.MinValue);
        var result = engine.Apply(Setting());
        Assert.False(result.Success);
        Assert.Contains("Rien n'a été modifié", result.Message);
        Assert.Equal(RegValue.DWord(5), _reg.Get(A));
        Assert.False(_backup.HasAnyEntry);
    }

    [Fact]
    public void Tout_annuler_defait_dans_l_ordre_inverse_meme_un_reglage_inconnu()
    {
        _reg.Values[A] = RegValue.DWord(5);
        var first = new RegistrySetting(Ctx, "first", "Premier", "", false, [new(A, RegValue.DWord(1))]);
        var second = new RegistrySetting(Ctx, "second", "Second", "", false, [new(A, RegValue.DWord(2))]);
        var engine = new SettingsEngine(Ctx, [first], () => DateTime.MinValue); // « second » inconnu de cette version
        engine.Apply(first);
        second.Apply();
        Assert.Equal(RegValue.DWord(2), _reg.Get(A));

        var results = engine.RevertAll();
        Assert.All(results, r => Assert.True(r.Success));
        Assert.Equal(RegValue.DWord(5), _reg.Get(A));   // ordre inverse : second (→1) puis first (→5)
        Assert.False(_backup.HasAnyEntry);
    }

    [Fact]
    public void Redemarrage_signale_puis_oublie_apres_un_redemarrage()
    {
        var now = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
        _backup = new BackupStore(File, _log, () => now);
        var boot = now.AddHours(-1);
        var engine = new SettingsEngine(Ctx, [Setting(restart: true)], () => boot);
        Assert.True(engine.Apply(Setting(restart: true)).RestartRequired);
        Assert.Equal(["Réglage s1"], engine.PendingRestartNames());

        boot = now.AddMinutes(5);                      // Windows a redémarré depuis
        Assert.Empty(engine.PendingRestartNames());
    }

    [Fact]
    public void Fichier_de_sauvegarde_abime_mis_de_cote_jamais_ecrase()
    {
        System.IO.File.WriteAllText(File, "{ pas du json");
        _backup = NewStore();
        Assert.NotNull(_backup.LoadWarning);
        Assert.False(System.IO.File.Exists(File));
        var aside = Directory.GetFiles(_dir.Path, "backup.illisible-*.json");
        Assert.Single(aside);
        Assert.Equal("{ pas du json", System.IO.File.ReadAllText(aside[0]));
    }

    [Fact]
    public void Chemin_de_sauvegarde_aller_retour()
    {
        Assert.Equal(B, RegPath.FromBackupKey(B.BackupKey));
        Assert.Null(RegPath.FromBackupKey("text:plan"));
    }
}
