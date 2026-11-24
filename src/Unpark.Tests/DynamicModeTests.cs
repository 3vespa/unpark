using Unpark.Core.Dynamic;
using Unpark.Core.Power;

namespace Unpark.Tests;

public sealed class FakeActivity : IActivityProbe
{
    public TimeSpan Idle { get; set; }
    public bool Fullscreen { get; set; }
    public TimeSpan IdleTime() => Idle;
    public bool FullscreenAppRunning() => Fullscreen;
}

public class DynamicModeTests : IDisposable
{
    private readonly FakePower _pc = new();
    private readonly FakeActivity _activity = new();
    private readonly MemoryLog _log = new();
    private readonly TempDir _dir = new();
    private readonly DynamicModeController _mode;

    public DynamicModeTests()
    {
        // Plan Unpark installé (copie de Performances élevées).
        _pc.Run("powercfg.exe", $"/duplicatescheme {PowerSettings.HighPerformance} {PowerSettings.Unpark}");
        _pc.Schemes[PowerSettings.Unpark].Name = "Unpark Performance";
        _mode = new DynamicModeController(_pc, _pc, _activity, _log) { Settings = new DynamicSettings(true, 10) };
    }

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Actif_Unpark_inactif_10_min_Equilibre_puis_retour()
    {
        _activity.Idle = TimeSpan.FromSeconds(3);
        Assert.Equal(DynamicState.Performance, _mode.Tick());
        Assert.Equal(PowerSettings.Unpark, _pc.Active);

        _activity.Idle = TimeSpan.FromMinutes(9);
        Assert.Equal(DynamicState.Performance, _mode.Tick());

        _activity.Idle = TimeSpan.FromMinutes(10);
        Assert.Equal(DynamicState.Economy, _mode.Tick());
        Assert.Equal(PowerSettings.Balanced, _pc.Active);
        Assert.Contains(_log.Lines, l => l.Contains("inactif depuis 10 min"));

        _activity.Idle = TimeSpan.FromSeconds(1);   // la souris bouge
        Assert.Equal(DynamicState.Performance, _mode.Tick());
        Assert.Equal(PowerSettings.Unpark, _pc.Active);
    }

    [Fact]
    public void Jeu_en_plein_ecran_a_la_manette_reste_en_Performance()
    {
        _activity.Idle = TimeSpan.FromMinutes(45);   // manette : Windows ne voit aucune activité
        _activity.Fullscreen = true;
        Assert.Equal(DynamicState.Performance, _mode.Tick());
        Assert.Equal(PowerSettings.Unpark, _pc.Active);
    }

    [Fact]
    public void Plan_change_a_la_main_met_en_pause_sans_se_battre()
    {
        _activity.Idle = TimeSpan.Zero;
        _mode.Tick();
        _pc.Active = PowerSettings.HighPerformance;     // l'utilisateur choisit un autre plan
        Assert.Equal(DynamicState.Paused, _mode.Tick());
        _activity.Idle = TimeSpan.FromHours(1);
        Assert.Equal(DynamicState.Paused, _mode.Tick());
        Assert.Equal(PowerSettings.HighPerformance, _pc.Active);   // pas touché

        _mode.Resume();
        Assert.Equal(DynamicState.Economy, _mode.Tick());
        Assert.Equal(PowerSettings.Balanced, _pc.Active);
    }

    [Fact]
    public void Forcer_un_plan_met_le_mode_automatique_en_pause()
    {
        _mode.Force(PowerSettings.Balanced, "Équilibré");
        Assert.Equal(PowerSettings.Balanced, _pc.Active);
        _activity.Idle = TimeSpan.Zero;
        Assert.Equal(DynamicState.Paused, _mode.Tick());
        Assert.Equal(PowerSettings.Balanced, _pc.Active);
        Assert.StartsWith("En pause", _mode.StatusText);
    }

    [Fact]
    public void Sans_plan_Unpark_ou_desactive_rien_ne_change()
    {
        _mode.Settings = new DynamicSettings(Enabled: false);
        Assert.Equal(DynamicState.Disabled, _mode.Tick());

        var pc = new FakePower();   // plan Unpark absent
        var mode = new DynamicModeController(pc, pc, _activity, _log) { Settings = new DynamicSettings() };
        Assert.Equal(DynamicState.NotInstalled, mode.Tick());
        Assert.Equal(PowerSettings.Balanced, pc.Active);
        Assert.Empty(pc.Commands);
    }

    [Fact]
    public void Reglages_enregistres_et_relus()
    {
        var store = new DynamicSettingsStore(Path.Combine(_dir.Path, "dynamic.json"));
        Assert.Equal(new DynamicSettings(), store.Load());          // défaut : activé, 10 min
        store.Save(new DynamicSettings(true, 30));
        Assert.Equal(TimeSpan.FromMinutes(30), store.Load().IdleThreshold);
        File.WriteAllText(store.File, "abîmé");
        Assert.Equal(new DynamicSettings(), store.Load());
    }
}

public sealed class FakeAutostart : IAutostartTask
{
    public string? Path { get; set; }
    public string? RegisteredPath() => Path;
    public void Register(string exePath) => Path = exePath;
    public void Delete() => Path = null;
}

public class DynamicAutostartTests : IDisposable
{
    private readonly TempDir _dir = new();
    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Lancement_avec_Windows_reversible_et_suit_l_emplacement_de_l_exe()
    {
        var log = new MemoryLog();
        var ctx = new Unpark.Core.Settings.SettingContext(new FakeRegistry(), new Unpark.Core.Backup.BackupStore(Path.Combine(_dir.Path, "b.json"), log), log);
        var task = new FakeAutostart();
        var exe = @"C:\Users\moi\Downloads\Unpark-v0.8.0.exe";
        var setting = new DynamicAutostartSetting(ctx, task, () => exe);

        Assert.False(setting.IsApplied());
        setting.Apply();
        Assert.Equal(exe, task.Path);
        Assert.True(setting.IsApplied());

        exe = @"C:\Jeux\Unpark-v0.9.0.exe";          // nouvelle version ailleurs : à recréer
        Assert.False(setting.IsApplied());

        setting.Revert();
        Assert.Null(task.Path);
        Assert.False(ctx.Backup.HasAnyEntry);
    }
}
