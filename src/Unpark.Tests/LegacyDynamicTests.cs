using Unpark.Core.Backup;
using Unpark.Core.Legacy;
using Unpark.Core.Registry;
using Unpark.Core.Settings;

namespace Unpark.Tests;

public sealed class FakeLegacyDynamic : ILegacyDynamicSystem
{
    public bool TaskExists { get; set; } = true;
    public bool TrayRunning { get; set; }
    public bool SettingsFileExists { get; set; } = true;
    public int DeleteCalls { get; private set; }

    public void DeleteAutostartTask()
    {
        DeleteCalls++;
        TaskExists = false;
    }

    public bool StopRunningTray()
    {
        var was = TrayRunning;
        TrayRunning = false;
        return was;
    }

    public void DeleteSettingsFile() => SettingsFileExists = false;
}

/// <summary>
/// Mode dynamique retiré en v0.9.1 : « Tout annuler » doit encore défaire ce que la v0.9.0 avait fait.
/// Les sauvegardes ci-dessous ont exactement le format écrit par la v0.9.0 dans %AppData%\Unpark\backup.json.
/// </summary>
public class LegacyDynamicTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FakeRegistry _reg = new();
    private readonly MemoryLog _log = new();
    private readonly FakeLegacyDynamic _system = new();

    private static readonly RegPath GameMode = new(RegHive.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled");

    public void Dispose() => _dir.Dispose();

    /// <summary>backup.json de la v0.9.0 : Mode Jeu appliqué (valeur absente avant), puis « Lancer le mode dynamique avec Windows ».</summary>
    private static string V090Backup(string existed) => $$"""
        {
          "FormatVersion": 1,
          "Entries": [
            {
              "SettingId": "gaming.gamemode",
              "SettingName": "Mode Jeu",
              "FirstAppliedUtc": "2026-11-20T18:02:11.123Z",
              "Originals": {
                "reg:HKCU\\Software\\Microsoft\\GameBar\\AutoGameModeEnabled": { "Exists": false }
              }
            },
            {
              "SettingId": "dynamic.autostart",
              "SettingName": "Lancer le mode dynamique avec Windows",
              "FirstAppliedUtc": "2026-11-20T18:05:42.456Z",
              "Originals": {
                "text:dynamic.autostartExisted": { "Exists": true, "Text": "{{existed}}" }
              }
            }
          ],
          "PendingRestarts": {},
          "RestorePointOfferedUtc": "2026-11-20T18:01:00Z"
        }
        """;

    private (SettingsEngine Engine, BackupStore Backup) Load(string json)
    {
        var file = Path.Combine(_dir.Path, "backup.json");
        File.WriteAllText(file, json);
        var backup = new BackupStore(file, _log);
        Assert.Null(backup.LoadWarning);
        var ctx = new SettingContext(_reg, backup, _log);
        var legacy = new LegacyDynamicAutostart(ctx, _system);
        // Même branchement que l'appli : le réglage n'est dans aucun catalogue, seulement retrouvé par « Tout annuler ».
        var engine = new SettingsEngine(ctx, [], () => DateTime.UtcNow,
            id => id == LegacyDynamicAutostart.SettingId ? legacy : null);
        return (engine, backup);
    }

    [Fact]
    public void Tout_annuler_supprime_la_tache_creee_par_la_v090_et_ferme_son_icone()
    {
        _reg.Set(GameMode, RegValue.DWord(1));
        _system.TrayRunning = true;
        var (engine, backup) = Load(V090Backup("non"));
        Assert.Equal(2, backup.Entries.Count);

        var results = engine.RevertAll();

        Assert.All(results, r => Assert.True(r.Success, r.Message));
        Assert.Equal(2, results.Count);
        Assert.False(_system.TaskExists);
        Assert.Equal(1, _system.DeleteCalls);
        Assert.False(_system.TrayRunning);
        Assert.False(_system.SettingsFileExists);
        Assert.Null(_reg.Get(GameMode));                 // l'autre réglage est bien annulé aussi
        Assert.False(backup.HasAnyEntry);

        // La sauvegarde vidée est bien écrite sur le disque.
        var reloaded = new BackupStore(Path.Combine(_dir.Path, "backup.json"), _log);
        Assert.False(reloaded.HasAnyEntry);
    }

    [Fact]
    public void Tout_annuler_garde_la_tache_si_elle_existait_avant_Unpark()
    {
        var (engine, backup) = Load(V090Backup("oui"));

        var results = engine.RevertAll();

        Assert.All(results, r => Assert.True(r.Success, r.Message));
        Assert.True(_system.TaskExists);
        Assert.Equal(0, _system.DeleteCalls);
        Assert.False(backup.HasAnyEntry);
    }

    [Fact]
    public void Sans_le_reglage_legacy_la_tache_resterait_en_place()
    {
        // Garde-fou : c'est ce qui arrivait si on supprimait simplement l'ancien réglage.
        var file = Path.Combine(_dir.Path, "backup.json");
        File.WriteAllText(file, V090Backup("non"));
        var backup = new BackupStore(file, _log);
        var engine = new SettingsEngine(new SettingContext(_reg, backup, _log), [], () => DateTime.UtcNow);

        engine.RevertAll();

        Assert.False(backup.HasAnyEntry);
        Assert.Equal(0, _system.DeleteCalls); // entrée effacée, mais tâche jamais supprimée
    }

    [Fact]
    public void Le_reglage_legacy_ne_peut_plus_etre_applique()
    {
        var (engine, backup) = Load("""{ "FormatVersion": 1, "Entries": [] }""");
        var legacy = new LegacyDynamicAutostart(new SettingContext(_reg, backup, _log), _system);

        Assert.False(legacy.IsApplied());
        var result = engine.Apply(legacy);

        Assert.False(result.Success);
        Assert.Equal(0, _system.DeleteCalls); // pas de sauvegarde : l'annulation automatique ne touche à rien
        Assert.False(backup.HasAnyEntry);
    }
}
