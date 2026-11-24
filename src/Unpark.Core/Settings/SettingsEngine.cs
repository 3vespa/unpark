using Unpark.Core.Logging;

namespace Unpark.Core.Settings;

public sealed record OperationResult(bool Success, string Message, bool RestartRequired = false);

/// <summary>
/// Applique / annule les réglages avec journal, gestion d'erreur et suivi des redémarrages.
/// Si Apply() échoue en cours de route, ce qui a déjà été modifié est remis à l'état d'origine.
/// </summary>
/// <param name="resolver">Retrouve un réglage créé à la volée (ex. un programme au démarrage) à partir de son identifiant.</param>
public sealed class SettingsEngine(
    SettingContext ctx, IEnumerable<ISetting> catalog, Func<DateTime> lastBootUtc, Func<string, ISetting?>? resolver = null)
{
    private readonly Dictionary<string, ISetting> _catalog = catalog.ToDictionary(s => s.Id);
    private IActionLog Log => ctx.Log;

    public IReadOnlyCollection<ISetting> Catalog => _catalog.Values;

    public OperationResult Apply(ISetting setting)
    {
        Log.Info($"Appliquer : {setting.Name} ({setting.Id})");
        try
        {
            setting.Apply();
        }
        catch (Exception e)
        {
            Log.Error($"Échec de « {setting.Name} », retour à l'état d'origine", e);
            try
            {
                setting.Revert();
            }
            catch (Exception e2)
            {
                Log.Error($"Le retour à l'état d'origine de « {setting.Name} » a aussi échoué", e2);
                return new(false, $"« {setting.Name} » a échoué et n'a pas pu être complètement annulé : {e.Message}. Utilise « Tout annuler » ou le point de restauration.");
            }
            return new(false, $"« {setting.Name} » n'a pas pu être appliqué ({e.Message}). Rien n'a été modifié.");
        }
        if (setting.RequiresRestart) ctx.Backup.MarkRestartNeeded(setting.Id);
        Log.Info($"OK : {setting.Name}{(setting.RequiresRestart ? " (redémarrage nécessaire)" : "")}");
        return new(true, $"« {setting.Name} » appliqué.", setting.RequiresRestart);
    }

    public OperationResult Revert(ISetting setting)
    {
        Log.Info($"Annuler : {setting.Name} ({setting.Id})");
        try
        {
            setting.Revert();
        }
        catch (Exception e)
        {
            Log.Error($"Échec de l'annulation de « {setting.Name} »", e);
            return new(false, $"« {setting.Name} » n'a pas pu être annulé : {e.Message}. La sauvegarde est conservée, tu peux réessayer.");
        }
        if (setting.RequiresRestart) ctx.Backup.MarkRestartNeeded(setting.Id);
        Log.Info($"OK : annulation de {setting.Name}");
        return new(true, $"« {setting.Name} » annulé : état d'origine remis.", setting.RequiresRestart);
    }

    /// <summary>Annule tout ce qui est sauvegardé, du plus récent au plus ancien.</summary>
    public IReadOnlyList<OperationResult> RevertAll()
    {
        Log.Info("Tout annuler");
        var results = new List<OperationResult>();
        foreach (var entry in ctx.Backup.Entries.Reverse())
        {
            if (_catalog.TryGetValue(entry.SettingId, out var setting) || (setting = Resolve(entry.SettingId)) is not null)
            {
                results.Add(Revert(setting));
                continue;
            }
            // Réglage inconnu de cette version : on remet quand même les valeurs de registre sauvegardées.
            try
            {
                RegistrySetting.RevertFromBackup(ctx, entry.SettingId, entry.SettingName);
                results.Add(new(true, $"« {entry.SettingName} » annulé."));
            }
            catch (Exception e)
            {
                Log.Error($"Échec de l'annulation de « {entry.SettingName} »", e);
                results.Add(new(false, $"« {entry.SettingName} » n'a pas pu être annulé : {e.Message}."));
            }
        }
        return results;
    }

    private ISetting? Resolve(string id)
    {
        try { return resolver?.Invoke(id); }
        catch (Exception e)
        {
            Log.Warn($"Réglage « {id} » introuvable : {e.Message}");
            return null;
        }
    }

    /// <summary>Noms des réglages qui attendent un redémarrage de Windows.</summary>
    public IReadOnlyList<string> PendingRestartNames() =>
        ctx.Backup.PendingRestarts(lastBootUtc())
            .Select(id => _catalog.TryGetValue(id, out var s) ? s.Name : id)
            .ToList();

    /// <summary>Heure du dernier démarrage de Windows (un « Arrêter » avec le démarrage rapide ne compte pas, un « Redémarrer » oui).</summary>
    public static DateTime SystemLastBootUtc() => DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
}
