using Unpark.Core.Logging;
using Unpark.Core.Registry;

namespace Unpark.Core.RestorePoints;

/// <summary>Accès à la restauration système de Windows (WMI). Remplacé par une fausse version dans les tests.</summary>
public interface IRestorePointApi
{
    /// <summary>Date (UTC) du point de restauration le plus récent, ou null s'il n'y en a aucun.</summary>
    DateTime? GetLatestUtc();
    /// <summary>Demande la création ; renvoie le code de retour Windows (0 = accepté, ce qui ne garantit PAS la création).</summary>
    uint Create(string description);
}

public enum RestorePointOutcome
{
    /// <summary>Point créé (vérifié en relisant la liste).</summary>
    Created,
    /// <summary>Un point existe déjà depuis moins de 24 h : Windows n'en créerait pas d'autre. Demander à l'utilisateur.</summary>
    RecentExists,
    /// <summary>Windows n'a rien créé : protection du système probablement désactivée sur C:.</summary>
    NotCreated,
    Failed,
}

public sealed record RestorePointResult(RestorePointOutcome Outcome, string Message, DateTime? LatestUtc);

/// <summary>
/// Création d'un point de restauration avant les modifications.
/// Piège géré : par défaut Windows refuse d'en créer un deuxième en moins de 24 h, et l'appel « réussit » quand même
/// sans rien créer. On vérifie donc toujours en relisant la liste. Pour forcer, la valeur documentée
/// SystemRestorePointCreationFrequency (minutes) est mise à 0 le temps de la création, puis remise à l'identique.
/// </summary>
public sealed class RestorePointService(IRestorePointApi api, IRegistry registry, IActionLog log, Func<DateTime>? utcNow = null)
{
    public static readonly RegPath FrequencyPath =
        new(RegHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", "SystemRestorePointCreationFrequency");

    /// <summary>Valeur par défaut de Windows quand la clé n'existe pas : 1440 minutes (24 h).</summary>
    private const int DefaultFrequencyMinutes = 1440;

    private readonly Func<DateTime> _utcNow = utcNow ?? (() => DateTime.UtcNow);

    public DateTime? GetLatestUtc()
    {
        try { return api.GetLatestUtc(); }
        catch (Exception e)
        {
            log.Warn($"Lecture des points de restauration impossible : {e.Message}");
            return null;
        }
    }

    public RestorePointResult Create(string description, bool forceIfRecent)
    {
        var before = GetLatestUtc();
        var frequency = registry.Get(FrequencyPath);
        var minutes = frequency is { Kind: RegKind.DWord } ? (int)frequency.Number : DefaultFrequencyMinutes;
        var recent = before is { } b && minutes > 0 && _utcNow() - b < TimeSpan.FromMinutes(minutes);

        if (recent && !forceIfRecent)
            return new(RestorePointOutcome.RecentExists,
                $"Un point de restauration existe déjà ({ToLocalText(before!.Value)}). Windows n'en crée pas d'autre avant {minutes / 60} h.", before);

        log.Info($"Création d'un point de restauration « {description} »{(recent ? " (forcée : un point récent existe)" : "")}");
        uint code;
        try
        {
            if (recent) registry.Set(FrequencyPath, RegValue.DWord(0));
            code = api.Create(description);
        }
        catch (Exception e)
        {
            log.Error("Création du point de restauration impossible", e);
            return new(RestorePointOutcome.Failed, $"Création impossible : {e.Message}", before);
        }
        finally
        {
            if (recent) RestoreFrequency(frequency);
        }

        var after = GetLatestUtc();
        if (after is not null && (before is null || after > before))
        {
            log.Info($"Point de restauration créé ({ToLocalText(after.Value)})");
            return new(RestorePointOutcome.Created, $"Point de restauration créé ({ToLocalText(after.Value)}).", after);
        }

        log.Warn($"Aucun point de restauration créé (code Windows {code})");
        return new(RestorePointOutcome.NotCreated,
            "Windows n'a pas créé de point de restauration. La protection du système est probablement désactivée sur C: : " +
            "Démarrer → « Créer un point de restauration » → sélectionne C: → Configurer → « Activer la protection du système ».",
            before);
    }

    /// <summary>Remet SystemRestorePointCreationFrequency exactement comme avant (supprimée si elle n'existait pas).</summary>
    private void RestoreFrequency(RegValue? original)
    {
        try
        {
            if (original is null) registry.Delete(FrequencyPath);
            else registry.Set(FrequencyPath, original);
        }
        catch (Exception e)
        {
            log.Error($"Impossible de remettre {FrequencyPath} à sa valeur d'origine ({original?.ToString() ?? "absente"})", e);
        }
    }

    private static string ToLocalText(DateTime utc) => utc.ToLocalTime().ToString("dd/MM/yyyy à HH:mm");
}
