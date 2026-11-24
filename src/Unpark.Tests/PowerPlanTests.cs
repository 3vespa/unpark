using Unpark.Core.Backup;
using Unpark.Core.Power;
using Unpark.Core.Settings;

namespace Unpark.Tests;

public class PowerPlanTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FakeRegistry _reg = new();
    private readonly MemoryLog _log = new();
    private readonly FakePower _pc = new();
    private BackupStore _backup;

    public PowerPlanTests() => _backup = new BackupStore(Path.Combine(_dir.Path, "backup.json"), _log);
    public void Dispose() => _dir.Dispose();

    private UnparkPowerPlan Plan() => new(new SettingContext(_reg, _backup, _log), _pc, _pc);
    private static Guid U => PowerSettings.Unpark;
    private static readonly PowerSettingDef MinCores = PowerSettings.All.First(d => d.Alias == "CPMINCORES");
    private static readonly PowerSettingDef Standby = PowerSettings.All.First(d => d.Alias == "STANDBYIDLE");

    [Fact]
    public void Installe_depuis_Performances_optimales_regle_et_active()
    {
        var plan = Plan();
        plan.Apply();

        Assert.Equal(U, _pc.Active);
        Assert.Equal("Unpark Performance", _pc.Schemes[U].Name);
        Assert.Contains(_pc.Commands, c => c.StartsWith($"/duplicatescheme {PowerSettings.Ultimate} {U}"));
        foreach (var d in PowerSettings.All)
            Assert.Equal(d.Value, _pc.ReadAc(U, d.SubGroup, d.Setting));
        Assert.True(plan.IsApplied());
        // Alias powercfg utilisés quand ils existent.
        Assert.Contains($"/setacvalueindex {U} SUB_PROCESSOR CPMINCORES 100", _pc.Commands);
        Assert.Contains("-attributes SUB_PROCESSOR CPMINCORES -ATTRIB_HIDE", _pc.Commands);
    }

    [Fact]
    public void Sans_Performances_optimales_utilise_Performances_elevees_puis_Equilibre()
    {
        _pc.CannotDuplicate.Add(PowerSettings.Ultimate);
        Plan().Apply();
        Assert.Contains(_log.Lines, l => l.Contains("à partir de « Performances élevées »"));

        var pc2 = new FakePower();
        pc2.CannotDuplicate.Add(PowerSettings.Ultimate);
        pc2.CannotDuplicate.Add(PowerSettings.HighPerformance);
        new UnparkPowerPlan(new SettingContext(_reg, new BackupStore(Path.Combine(_dir.Path, "b2.json"), _log), _log), pc2, pc2).Apply();
        Assert.Equal(U, pc2.Active);
        Assert.Equal(100u, pc2.ReadAc(U, MinCores.SubGroup, MinCores.Setting));
    }

    [Fact]
    public void Supprimer_revient_a_l_ancien_plan_supprime_Unpark_et_recache()
    {
        _pc.Active = PowerSettings.HighPerformance;
        var plan = Plan();
        plan.Apply();
        Assert.DoesNotContain(MinCores.Setting, _pc.Hidden);

        plan.Revert();
        Assert.Equal(PowerSettings.HighPerformance, _pc.Active);
        Assert.False(_pc.Schemes.ContainsKey(U));
        Assert.Contains(MinCores.Setting, _pc.Hidden);
        Assert.False(_backup.HasAnyEntry);
    }

    [Fact]
    public void Revenir_a_l_ancien_plan_garde_Unpark_installe_et_la_sauvegarde()
    {
        var plan = Plan();
        plan.Apply();
        plan.SwitchToPrevious();
        Assert.Equal(PowerSettings.Balanced, _pc.Active);
        Assert.True(plan.IsInstalled());
        Assert.False(plan.IsApplied());

        // Réactivation puis suppression : l'ancien plan mémorisé reste celui d'avant Unpark.
        plan.Apply();
        Assert.Equal(U, _pc.Active);
        plan.Revert();
        Assert.Equal(PowerSettings.Balanced, _pc.Active);
    }

    [Fact]
    public void Portable_valeurs_agressives_sur_secteur_seulement()
    {
        _pc.Battery = true;
        _pc.Schemes[PowerSettings.Balanced].Values[(Standby.SubGroup, Standby.Setting)] = (900, 600);
        Plan().Apply();
        Assert.Equal(0u, _pc.ReadAc(U, Standby.SubGroup, Standby.Setting));     // secteur : jamais
        Assert.Equal(600u, _pc.ReadDc(U, Standby.SubGroup, Standby.Setting));   // batterie : comme Équilibré
    }

    [Fact]
    public void Variantes_coeurs_efficaces_ignorees_si_absentes()
    {
        var pc = new FakePower();
        var optional = PowerSettings.All.Where(d => d.Optional).ToList();
        foreach (var d in optional) pc.MissingSettings.Add(d.Setting);
        pc.Templates[PowerSettings.Ultimate] = pc.MakeScheme("Performances optimales", 77, 77);
        var plan = new UnparkPowerPlan(new SettingContext(_reg, _backup, _log), pc, pc);
        plan.Apply();
        Assert.Equal(U, pc.Active);
        Assert.DoesNotContain(pc.Commands, c => optional.Any(d => c.Contains(d.Setting.ToString())));
        Assert.True(plan.IsApplied());
        Assert.DoesNotContain(plan.GetStatus().Settings, s => s.Def.Optional);
    }

    [Fact]
    public void Ancien_plan_supprime_entre_temps_retour_sur_Equilibre()
    {
        _pc.Schemes[new Guid("11111111-2222-3333-4444-555555555555")] = _pc.MakeScheme("Mon plan", 1, 1);
        _pc.Active = new Guid("11111111-2222-3333-4444-555555555555");
        var plan = Plan();
        plan.Apply();
        _pc.Schemes.Remove(new Guid("11111111-2222-3333-4444-555555555555"));
        plan.Revert();
        Assert.Equal(PowerSettings.Balanced, _pc.Active);
        Assert.False(_pc.Schemes.ContainsKey(U));
    }

    [Fact]
    public void Echec_en_cours_remet_tout_comme_avant_via_le_moteur()
    {
        _pc.Active = PowerSettings.HighPerformance;
        _pc.MissingSettings.Clear();
        var failing = new FailingSetactive(_pc);
        var plan = new UnparkPowerPlan(new SettingContext(_reg, _backup, _log), _pc, failing);
        var engine = new SettingsEngine(new SettingContext(_reg, _backup, _log), [plan], () => DateTime.MinValue);
        var result = engine.Apply(plan);
        Assert.False(result.Success);
        Assert.Equal(PowerSettings.HighPerformance, _pc.Active);
        Assert.False(_pc.Schemes.ContainsKey(U));
        Assert.Contains(MinCores.Setting, _pc.Hidden);
        Assert.False(_backup.HasAnyEntry);
    }

    /// <summary>powercfg qui refuse d'activer le plan Unpark (simule une erreur Windows en fin d'installation).</summary>
    private sealed class FailingSetactive(FakePower inner) : Unpark.Core.Commands.ICommandRunner
    {
        public Unpark.Core.Commands.CommandResult Run(string program, string arguments) =>
            arguments == $"/setactive {PowerSettings.Unpark}" ? new(1, "", "refusé (simulé)") : inner.Run(program, arguments);
    }
}

public class PowerFormatTests
{
    [Fact]
    public void Valeurs_lisibles_en_francais()
    {
        var all = PowerSettings.All;
        Assert.Equal("100 %", all.First(d => d.Alias == "CPMINCORES").Format(100));
        Assert.Equal("Agressif", all.First(d => d.Alias == "PERFBOOSTMODE").Format(2));
        Assert.Equal("Jamais", all.First(d => d.Alias == "STANDBYIDLE").Format(0));
        Assert.Equal("20 min", all.First(d => d.Alias == "DISKIDLE").Format(1200));
        Assert.Equal("Économies maximales", all.First(d => d.Alias == "ASPM").Format(2));
        Assert.Equal("—", all[0].Format(null));
    }
}
