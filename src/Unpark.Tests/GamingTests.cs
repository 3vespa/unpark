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
        _settings = GamingSettings.Create(new SettingContext(_reg, _backup, _log), _ui, windowsBuild: 26200);
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
    public void Effets_visuels_retires_mais_encore_annulables()
    {
        Assert.DoesNotContain(_settings, s => s.Id == GamingSettings.VisualFxId);
        var ctx = new SettingContext(_reg, _backup, _log);
        var legacy = GamingSettings.Legacy(ctx, _ui).Single();
        legacy.Apply();                                   // appliqué avec une ancienne version d'Unpark
        Assert.All(_ui.Effects.Values, Assert.False);
        var engine = new SettingsEngine(ctx, [.. _settings, .. GamingSettings.Legacy(ctx, _ui)], () => DateTime.MinValue);
        Assert.All(engine.RevertAll(), r => Assert.True(r.Success));
        Assert.All(_ui.Effects.Values, Assert.True);
    }

    [Fact]
    public void Jeux_en_fenetre_garde_les_autres_reglages_DirectX()
    {
        _reg.Values[WindowedGamesSetting.ValuePath] = RegValue.String("AutoHDREnable=1;VRROptimizeEnable=0;");
        var s = Get(GamingSettings.WindowedGamesId);
        Assert.False(s.IsApplied());
        s.Apply();
        Assert.Equal("AutoHDREnable=1;VRROptimizeEnable=0;SwapEffectUpgradeEnable=1;", _reg.Get(WindowedGamesSetting.ValuePath)!.Text);
        s.Revert();
        Assert.Equal("AutoHDREnable=1;VRROptimizeEnable=0;", _reg.Get(WindowedGamesSetting.ValuePath)!.Text);
    }

    [Fact]
    public void Jeux_en_fenetre_indisponible_sur_Windows_10()
    {
        var ctx = new SettingContext(_reg, _backup, _log);
        var win10 = GamingSettings.Create(ctx, _ui, windowsBuild: 19045).Single(s => s.Id == GamingSettings.WindowedGamesId);
        Assert.NotNull(((IConditionalSetting)win10).UnavailableReason());
        var result = new SettingsEngine(ctx, [win10], () => DateTime.MinValue).Apply(win10);
        Assert.False(result.Success);
        Assert.Null(_reg.Get(WindowedGamesSetting.ValuePath));
    }

    [Fact]
    public void Integrite_de_la_memoire_avertissement_redemarrage_et_verrous()
    {
        var hvci = Get(GamingSettings.MemoryIntegrityId);
        Assert.True(hvci.RequiresRestart);
        Assert.Contains("sécurité", ((IWarnedSetting)hvci).Warning);

        _reg.Values[MemoryIntegritySetting.EnabledPath] = RegValue.DWord(1);
        Assert.False(hvci.IsApplied());
        hvci.Apply();
        Assert.Equal(RegValue.DWord(0), _reg.Get(MemoryIntegritySetting.EnabledPath));
        hvci.Revert();
        Assert.Equal(RegValue.DWord(1), _reg.Get(MemoryIntegritySetting.EnabledPath));

        _reg.Values[MemoryIntegritySetting.LockedPath] = RegValue.DWord(1);
        Assert.Contains("Verrouillée", ((IConditionalSetting)hvci).UnavailableReason());
        Assert.Throws<InvalidOperationException>(hvci.Apply);
        Assert.Equal(RegValue.DWord(1), _reg.Get(MemoryIntegritySetting.EnabledPath));

        _reg.Values.Remove(MemoryIntegritySetting.LockedPath);
        _reg.Values[MemoryIntegritySetting.PolicyPath] = RegValue.DWord(2);
        Assert.Contains("stratégie", ((IConditionalSetting)hvci).UnavailableReason());
    }
}

public class HagsAdviceTests
{
    [Fact]
    public void Recommande_seulement_sur_RTX_40_et_50()
    {
        var rtx = HagsAdvice.For(["Intel(R) UHD Graphics 770", "NVIDIA GeForce RTX 4070 Ti"]);
        Assert.True(rtx.Recommended);
        Assert.Contains("DLSS Frame Generation", rtx.Note);

        var other = HagsAdvice.For(["NVIDIA GeForce RTX 3060"]);
        Assert.False(other.Recommended);
        Assert.Equal(HagsAdvice.NeutralNote, other.Note);
        Assert.False(HagsAdvice.For([]).Recommended);
    }
}
