using Unpark.Core.Registry;
using Unpark.Core.RestorePoints;

namespace Unpark.Tests;

public class RestorePointTests
{
    private readonly FakeRegistry _reg = new();
    private readonly FakeRestoreApi _api = new();
    private readonly MemoryLog _log = new();
    private static readonly DateTime Now = new(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);

    private RestorePointService Service() => new(_api, _reg, _log, () => Now, _ => { });

    [Fact]
    public void Cree_et_verifie_en_relisant_la_liste()
    {
        _api.Latest = Now.AddDays(-3);
        _api.OnCreate = () => Now;
        var r = Service().Create("Avant Unpark", forceIfRecent: false);
        Assert.Equal(RestorePointOutcome.Created, r.Outcome);
    }

    [Fact]
    public void Point_recent_demande_une_decision_sans_rien_creer()
    {
        _api.Latest = Now.AddHours(-2);
        var r = Service().Create("Avant Unpark", forceIfRecent: false);
        Assert.Equal(RestorePointOutcome.RecentExists, r.Outcome);
        Assert.Equal(0, _api.Calls);
    }

    [Fact]
    public void Forcer_met_la_frequence_a_zero_puis_la_remet_exactement()
    {
        _api.Latest = Now.AddHours(-2);
        RegValue? during = null;
        _api.DuringCreate = () => during = _reg.Get(RestorePointService.FrequencyPath);
        _api.OnCreate = () => Now;

        var r = Service().Create("Avant Unpark", forceIfRecent: true);
        Assert.Equal(RestorePointOutcome.Created, r.Outcome);
        Assert.Equal(RegValue.DWord(0), during);
        Assert.Null(_reg.Get(RestorePointService.FrequencyPath));   // n'existait pas → supprimée

        _reg.Values[RestorePointService.FrequencyPath] = RegValue.DWord(720);
        _api.Latest = Now.AddHours(-2);
        Service().Create("Avant Unpark", forceIfRecent: true);
        Assert.Equal(RegValue.DWord(720), _reg.Get(RestorePointService.FrequencyPath));
    }

    [Fact]
    public void Rien_cree_signale_la_protection_desactivee()
    {
        _api.Latest = null;
        _api.OnCreate = () => null;      // Windows « accepte » mais ne crée rien
        var r = Service().Create("Avant Unpark", forceIfRecent: false);
        Assert.Equal(RestorePointOutcome.NotCreated, r.Outcome);
        Assert.Contains("protection du système", r.Message);
    }

    [Fact]
    public void Frequence_personnalisee_respectee()
    {
        _reg.Values[RestorePointService.FrequencyPath] = RegValue.DWord(0);  // l'utilisateur a déjà levé la limite
        _api.Latest = Now.AddMinutes(-10);
        _api.OnCreate = () => Now;
        Assert.Equal(RestorePointOutcome.Created, Service().Create("x", forceIfRecent: false).Outcome);
    }

    [Fact]
    public void WMI_en_echec_la_methode_de_secours_prend_le_relais()
    {
        _api.Latest = Now.AddDays(-3);
        _api.WmiThrows = new InvalidOperationException("Type incompatible");
        _api.OnCreate = () => Now;
        var r = Service().Create("Avant Unpark", forceIfRecent: false);
        Assert.Equal(RestorePointOutcome.Created, r.Outcome);
        Assert.Equal(1, _api.FallbackCalls);
    }

    [Fact]
    public void Les_deux_methodes_en_echec_donnent_le_detail_technique()
    {
        _api.Latest = Now.AddDays(-3);
        _api.WmiThrows = new InvalidOperationException("Type incompatible");
        _api.FallbackCode = 1058;
        var r = Service().Create("Avant Unpark", forceIfRecent: false);
        Assert.Equal(RestorePointOutcome.Failed, r.Outcome);
        Assert.Contains("Type incompatible", r.Message);
        Assert.Contains("1058", r.Message);
    }
}
