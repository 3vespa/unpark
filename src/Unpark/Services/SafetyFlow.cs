using Unpark.Core.Gaming;
using Unpark.Core.RestorePoints;
using Unpark.Core.Settings;

namespace Unpark.Services;

/// <summary>
/// Parcours de sécurité commun à tous les écrans :
/// point de restauration proposé avant la première modification, puis application / annulation avec messages clairs.
/// </summary>
public static class SafetyFlow
{
    private static bool _offeredThisSession;
    private static AppServices S => AppServices.Current;

    /// <summary>Avant une modification : propose le point de restauration si aucun réglage Unpark n'est actif. false = abandon.</summary>
    public static async Task<bool> BeforeFirstChangeAsync()
    {
        if (S.Backup.HasAnyEntry || _offeredThisSession) return true;
        _offeredThisSession = true;

        var answer = Dialogs.Ask(
            "Première modification avec Unpark.\n\n" +
            "Créer un point de restauration Windows avant ? C'est recommandé : en cas de souci, il remet le PC dans l'état d'avant. " +
            "La création prend de 30 secondes à 1 minute.\n\n" +
            "Oui : créer le point, puis continuer\nNon : continuer sans\nAnnuler : ne rien modifier");
        if (answer == System.Windows.MessageBoxResult.Cancel) return false;
        S.Backup.MarkRestorePointOffered();
        if (answer == System.Windows.MessageBoxResult.No)
        {
            S.Log.Info("Point de restauration proposé : refusé par l'utilisateur");
            return true;
        }

        var ok = await CreateRestorePointAsync();
        return ok || Dialogs.Confirm("Pas de nouveau point de restauration. Continuer quand même la modification ?\n\n(Chaque réglage reste annulable avec Unpark.)");
    }

    /// <summary>Crée un point de restauration en gérant la limite de 24 h. true si un point récent est disponible.</summary>
    public static async Task<bool> CreateRestorePointAsync()
    {
        const string busy = "Création du point de restauration… (30 s à 1 min)";
        var result = await S.RunBusyAsync(busy, () => S.RestorePoints.Create("Avant modifications Unpark", forceIfRecent: false));

        if (result.Outcome == RestorePointOutcome.RecentExists)
        {
            if (!Dialogs.Confirm(result.Message + "\n\nEn créer un nouveau quand même ?\n\nNon : garder ce point récent (suffisant si tu n'as rien installé depuis)."))
            {
                S.Log.Info("Point de restauration récent conservé");
                S.NotifyChanged();
                return true;
            }
            result = await S.RunBusyAsync(busy, () => S.RestorePoints.Create("Avant modifications Unpark", forceIfRecent: true));
        }

        S.NotifyChanged();
        switch (result.Outcome)
        {
            case RestorePointOutcome.Created:
                Dialogs.Info(result.Message);
                return true;
            case RestorePointOutcome.NotCreated:
                Dialogs.Warn(result.Message);
                return false;
            default:
                Dialogs.Error(result.Message);
                return false;
        }
    }

    /// <summary>Réglage indisponible sur ce PC, ou avertissement refusé : false = ne rien faire.</summary>
    private static bool CanApply(ISetting setting)
    {
        if (setting is IConditionalSetting c && c.UnavailableReason() is { } reason)
        {
            Dialogs.Info($"« {setting.Name} » : {reason}");
            return false;
        }
        return setting is not IWarnedSetting w || Dialogs.Confirm(w.Warning);
    }

    public static async Task ApplyAsync(ISetting setting)
    {
        if (!CanApply(setting)) return;
        if (!await BeforeFirstChangeAsync()) return;
        var result = await S.RunBusyAsync($"Application : {setting.Name}…", () => S.Engine.Apply(setting));
        S.NotifyChanged();
        Report(result);
    }

    /// <summary>Applique plusieurs réglages à la suite (point de restauration proposé une seule fois), puis affiche un récapitulatif.</summary>
    public static async Task<IReadOnlyList<OperationResult>> ApplyManyAsync(IReadOnlyList<ISetting> settings, bool showSummary = true)
    {
        settings = settings.Where(CanApply).ToList();
        if (settings.Count == 0 || !await BeforeFirstChangeAsync()) return [];
        var results = await S.RunBusyAsync($"Application de {settings.Count} réglage(s)…",
            () => settings.Select(s => S.Engine.Apply(s)).ToList());
        S.NotifyChanged();
        if (showSummary)
        {
            var failed = results.Where(r => !r.Success).ToList();
            var restart = results.Any(r => r.Success && r.RestartRequired) ? "\n\nRedémarre le PC pour terminer." : "";
            if (failed.Count == 0) Dialogs.Info($"{results.Count} réglage(s) appliqué(s).{restart}");
            else Dialogs.Warn($"{results.Count - failed.Count} réglage(s) appliqué(s), {failed.Count} en échec :\n\n{string.Join("\n", failed.Select(f => "• " + f.Message))}{restart}");
        }
        return results;
    }

    public static async Task RevertAsync(ISetting setting)
    {
        var result = await S.RunBusyAsync($"Annulation : {setting.Name}…", () => S.Engine.Revert(setting));
        S.NotifyChanged();
        Report(result);
    }

    public static async Task RevertAllAsync()
    {
        var entries = S.Backup.Entries;
        if (entries.Count == 0)
        {
            Dialogs.Info("Rien à annuler : aucun réglage n'a été modifié par Unpark.");
            return;
        }
        var list = string.Join("\n", entries.Select(e => "• " + e.SettingName));
        if (!Dialogs.Confirm($"Remettre l'état d'origine pour :\n\n{list}\n\nContinuer ?")) return;

        var results = await S.RunBusyAsync("Annulation de tous les réglages…", () => S.Engine.RevertAll());
        S.NotifyChanged();
        var failed = results.Where(r => !r.Success).ToList();
        var restart = results.Any(r => r.RestartRequired) ? "\n\nRedémarre le PC pour terminer." : "";
        if (failed.Count == 0) Dialogs.Info($"Tout est annulé : état d'origine remis ({results.Count} réglage(s)).{restart}");
        else Dialogs.Warn($"{results.Count - failed.Count} réglage(s) annulé(s), {failed.Count} en échec :\n\n{string.Join("\n", failed.Select(f => "• " + f.Message))}\n\nDétails dans le journal.{restart}");
    }

    private static void Report(OperationResult result)
    {
        if (!result.Success) Dialogs.Error(result.Message);
        else if (result.RestartRequired) Dialogs.Info(result.Message + "\n\nRedémarre le PC pour que ce réglage prenne effet.");
    }
}
