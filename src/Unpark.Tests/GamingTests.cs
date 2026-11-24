using Unpark.Core.Backup;
using Unpark.Core.Gaming;
using Unpark.Core.Registry;
using Unpark.Core.Settings;

namespace Unpark.Tests;

/// <summary>Faux SystemParametersInfo : la souris écrit dans le faux registre, comme Windows (SPIF_UPDATEINIFILE).</summary>
public sealed class FakeSystemUi(FakeRegistry reg) : ISystemUiApi
{
    private const string Mouse = @"Control Panel\Mouse";
    public Dictionary<uint, bool> Effects { get; } = VisualEffectsSetting.Effects.ToDictionary(e => e.SetAction, _ => true);

    public MouseParams GetMouse() => new(Int("MouseThreshold1"), Int("MouseThreshold2"), Int("MouseSpeed"));

    public void SetMouse(MouseParams v)
    {
        reg.Set(new RegPath(RegHive.CurrentUser, Mouse, "MouseThreshold1"), RegValue.String(v.Threshold1.ToString()));
        reg.Set(new RegPath(RegHive.CurrentUser, Mouse, "MouseThreshold2"), RegValue.String(v.Threshold2.ToString()));
        reg.Set(new RegPath(RegHive.CurrentUser, Mouse, "MouseSpeed"), RegValue.String(v.Speed.ToString()));
    }

    public bool GetEffect(VisualEffect effect) => Effects[effect.SetAction];
    public void SetEffect(VisualEffect effect, bool on) => Effects[effect.SetAction] = on;

    private int Int(string name) => int.Parse(reg.Get(new RegPath(RegHive.CurrentUser, Mouse, name))?.Text ?? "0");
}

public class GamingTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FakeRegistry _reg = new();
    private readonly MemoryLog _log = new();
    private readonly BackupStore _backup;
    private readonly FakeSystemUi _ui;
    private readonly IReadOnlyList<ISetting> _settings;

    public GamingTests()
    {
        _backup = new BackupStore(Path.Combine(_dir.Path, "backup.json"), _log);
        _ui = new FakeSystemUi(_reg);
        _ui.SetMouse(new MouseParams(6, 10, 1)); // réglage Windows par défaut : accélération activée
        _settings = GamingSettings.Create(new SettingContext(_reg, _backup, _log), _ui);
    }

    public void Dispose() => _dir.Dispose();
    private ISetting Get(string id) => _settings.Single(s => s.Id == id);

    [Fact]
    public void Tous_les_reglages_Gaming_s_annulent_exactement()
    {
        var hags = new RegPath(RegHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode");
        _reg.Values[hags] = RegValue.DWord(1);
        var snapshot = new Dictionary<RegPath, RegValue>(_reg.Values);
        var effects = new Dictionary<uint, bool>(_ui.Effects) { [0x1025] = false }; // une ombre déjà coupée
        _ui.Effects[0x1025] = false;

        foreach (var s in _settings) { s.Apply(); Assert.True(s.IsApplied(), s.Name); }
        Assert.Equal(RegValue.DWord(2), _reg.Get(hags));
        Assert.Equal(new MouseParams(0, 0, 0), _ui.GetMouse());
        Assert.All(_ui.Effects.Values, Assert.False);

        foreach (var s in _settings.Reverse()) s.Revert();
        Assert.Equal(snapshot.OrderBy(k => k.Key.ToString()).Select(k => (k.Key, k.Value.ToString())),
                     _reg.Values.OrderBy(k => k.Key.ToString()).Select(k => (k.Key, k.Value.ToString())));
        Assert.Equal(effects, _ui.Effects);
        Assert.False(_backup.HasAnyEntry);
    }

    [Fact]
    public void HAGS_demande_un_redemarrage()
    {
        Assert.True(Get(GamingSettings.HagsId).RequiresRestart);
        Assert.False(Get(GamingSettings.GameModeId).RequiresRestart);
    }

    [Fact]
    public void Souris_remise_avec_les_valeurs_personnelles_de_l_utilisateur()
    {
        _ui.SetMouse(new MouseParams(4, 12, 2));
        var mouse = Get(GamingSettings.MouseId);
        mouse.Apply();
        mouse.Revert();
        Assert.Equal(new MouseParams(4, 12, 2), _ui.GetMouse());
    }

    [Fact]
    public void Apercu_des_effets_avant_application()
    {
        var fx = (VisualEffectsSetting)Get(GamingSettings.VisualFxId);
        _ui.Effects[0x1003] = false;
        var preview = fx.Preview();
        Assert.Equal(VisualEffectsSetting.Effects.Count, preview.Count);
        Assert.Contains(preview, p => p.Effect.SetAction == 0x1003 && !p.On);
        Assert.Contains(preview, p => p.Effect.SetAction == 0x1043 && p.On);
    }
}
