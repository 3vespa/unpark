using System.Text.Json;
using Unpark.Core.Commands;
using Unpark.Core.Logging;
using Unpark.Core.Power;

namespace Unpark.Core.Dynamic;

/// <summary>Réglages du mode dynamique (%AppData%\Unpark\dynamic.json).</summary>
public sealed record DynamicSettings(bool Enabled = true, int IdleMinutes = 10)
{
    public static readonly int[] IdleChoices = [5, 10, 15, 20, 30, 60];
    public TimeSpan IdleThreshold => TimeSpan.FromMinutes(Math.Clamp(IdleMinutes, 1, 240));
}

public sealed class DynamicSettingsStore(string file)
{
    public string File => file;

    public DynamicSettings Load()
    {
        try
        {
            return System.IO.File.Exists(file) ? JsonSerializer.Deserialize<DynamicSettings>(System.IO.File.ReadAllText(file)) ?? new() : new();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new DynamicSettings();
        }
    }

    public void Save(DynamicSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var tmp = file + ".tmp";
        System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        System.IO.File.Move(tmp, file, overwrite: true);
    }

    public DateTime LastWriteUtc => System.IO.File.Exists(file) ? System.IO.File.GetLastWriteTimeUtc(file) : DateTime.MinValue;
}

/// <summary>Activité de l'utilisateur. Remplacé par une fausse version dans les tests.</summary>
public interface IActivityProbe
{
    /// <summary>Temps depuis la dernière action au clavier ou à la souris (une manette n'est PAS vue par Windows).</summary>
    TimeSpan IdleTime();
    /// <summary>Une appli en plein écran (jeu, vidéo) est au premier plan : compte comme une activité (cas de la manette).</summary>
    bool FullscreenAppRunning();
}

public enum DynamicState
{
    /// <summary>Mode dynamique désactivé dans les réglages.</summary>
    Disabled,
    /// <summary>Le plan Unpark Performance n'est pas installé : rien à faire.</summary>
    NotInstalled,
    /// <summary>Utilisateur actif : Unpark Performance.</summary>
    Performance,
    /// <summary>Inactif depuis X minutes : Équilibré.</summary>
    Economy,
    /// <summary>En pause (demandée, ou plan changé à la main).</summary>
    Paused,
}

/// <summary>
/// Mode dynamique : Unpark Performance quand l'utilisateur est actif, Équilibré après X minutes d'inactivité.
/// Respecte l'utilisateur : s'il change de plan à la main, le mode se met en pause jusqu'à ce qu'il le relance.
/// À appeler régulièrement (Tick toutes les quelques secondes).
/// </summary>
public sealed class DynamicModeController(IPowerApi power, ICommandRunner commands, IActivityProbe activity, IActionLog log)
{
    private Guid? _lastSet;

    public DynamicSettings Settings { get; set; } = new();
    public DynamicState State { get; private set; } = DynamicState.Disabled;
    /// <summary>Pourquoi le mode est en pause (null sinon).</summary>
    public string? PauseReason { get; private set; }

    /// <summary>Met en pause (menu « Mettre en pause », ou plan forcé à la main).</summary>
    public void Pause(string reason)
    {
        PauseReason = reason;
        State = DynamicState.Paused;
        log.Info($"Mode dynamique en pause : {reason}");
    }

    public void Resume()
    {
        PauseReason = null;
        _lastSet = null; // on repart de l'état actuel sans le considérer comme un choix manuel
        State = DynamicState.Disabled;
        log.Info("Mode dynamique : reprise");
    }

    /// <summary>Force un plan (menu de la zone de notification) : met le mode automatique en pause.</summary>
    public void Force(Guid scheme, string name)
    {
        SetActive(scheme, $"forcé à la main : {name}");
        Pause($"plan « {name} » choisi depuis le menu");
    }

    public DynamicState Tick()
    {
        if (!Settings.Enabled) return State = DynamicState.Disabled;
        if (PauseReason is not null) return State = DynamicState.Paused;
        if (!power.ListSchemes().Contains(PowerSettings.Unpark)) return State = DynamicState.NotInstalled;

        var active = power.GetActiveScheme();
        if (_lastSet is { } last && active != last)
        {
            // Quelqu'un (l'utilisateur, un autre logiciel) a changé de plan : on ne se bat pas avec lui.
            Pause($"plan changé à la main (« {power.GetSchemeName(active) ?? active.ToString()} »)");
            return State;
        }

        var idle = activity.IdleTime();
        var economy = idle >= Settings.IdleThreshold && !activity.FullscreenAppRunning();
        var target = economy ? PowerSettings.Balanced : PowerSettings.Unpark;
        if (active != target)
            SetActive(target, economy ? $"inactif depuis {idle.TotalMinutes:0} min" : "activité détectée");
        _lastSet = target;
        return State = economy ? DynamicState.Economy : DynamicState.Performance;
    }

    private void SetActive(Guid scheme, string why)
    {
        var result = commands.Run("powercfg.exe", $"/setactive {scheme}");
        var name = power.GetSchemeName(scheme) ?? scheme.ToString();
        if (result.Ok) log.Info($"Mode dynamique : {why} → plan « {name} »");
        else log.Warn($"Mode dynamique : passage au plan « {name} » refusé ({result.Text})");
    }

    /// <summary>Texte de l'infobulle de l'icône.</summary>
    public string StatusText => State switch
    {
        DynamicState.Performance => "Unpark Performance (tu es actif)",
        DynamicState.Economy => $"Équilibré (inactif depuis {Settings.IdleMinutes} min ou plus)",
        DynamicState.Paused => $"En pause : {PauseReason}",
        DynamicState.NotInstalled => "Plan Unpark Performance non installé",
        _ => "Désactivé",
    };
}
