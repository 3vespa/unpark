using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Unpark.Core;
using Unpark.Core.Cleanup;
using Unpark.Core.Gaming;
using Unpark.Core.Settings;
using Unpark.Services;

namespace Unpark.ViewModels;

/// <summary>Une ligne du récapitulatif : un réglage, coché ou non, déjà en place ou non.</summary>
public sealed partial class OneClickItemViewModel : ObservableObject
{
    public OneClickItemViewModel(ISetting setting, bool defaultChecked, string? note)
    {
        Setting = setting;
        _note = note;
        _isChecked = defaultChecked;
        Refresh();
    }

    public ISetting Setting { get; }
    public string Name => Setting.Name;
    public string Description => Setting.Description;
    [ObservableProperty] private string? _note;
    public bool RequiresRestart => Setting.RequiresRestart;

    [ObservableProperty] private bool _isChecked;
    [ObservableProperty] private bool _alreadyApplied;
    [ObservableProperty] private string _stateText = "";

    public void Refresh()
    {
        try { AlreadyApplied = Setting.IsApplied(); }
        catch { AlreadyApplied = false; }
        StateText = AlreadyApplied ? "Déjà en place" : "Sera appliqué";
    }
}

/// <summary>
/// Profil « Gaming en 1 clic » : plan Unpark Performance + module Gaming (effets visuels en option),
/// récapitulatif avant de valider, nettoyage proposé à la fin, et « Tout annuler ».
/// </summary>
public sealed partial class OneClickViewModel : PageViewModel
{
    private static AppServices S => AppServices.Current;

    public OneClickViewModel() : base("Gaming en 1 clic", "")
    {
        Items.Add(new OneClickItemViewModel(S.PowerPlan, true, null));
        // Intégrité de la mémoire exclue (compromis de sécurité : seulement depuis la page Gaming, avec avertissement).
        // HAGS jamais coché d'office : coché seulement sur RTX 40/50 (voir LoadHagsAdviceAsync). Souris décochée : confort, pas performances.
        foreach (var s in S.Gaming.Where(s => s.Id != GamingSettings.MemoryIntegrityId)
                                  .Where(s => s is not IConditionalSetting c || c.UnavailableReason() is null))
            Items.Add(new OneClickItemViewModel(s, s.Id is not (GamingSettings.HagsId or GamingSettings.MouseId),
                s.Id == GamingSettings.MouseId ? "Optionnel : aucun effet sur les FPS, rend seulement la visée constante." : null));
        S.StateChanged += Refresh;
        Refresh();
        _ = DetectLaptopAsync();
        _ = LoadHagsAdviceAsync();
    }

    private async Task LoadHagsAdviceAsync()
    {
        var advice = await S.HagsAdviceAsync();
        var hags = Items.Single(i => i.Setting.Id == GamingSettings.HagsId);
        hags.Note = advice.Note;
        hags.IsChecked = advice.Recommended;
    }

    public ObservableCollection<OneClickItemViewModel> Items { get; } = [];
    [ObservableProperty] private bool _proposeCleanup = true;
    [ObservableProperty] private bool _isLaptop;
    [ObservableProperty] private string _activeChangesText = "";

    private async Task DetectLaptopAsync()
    {
        try { IsLaptop = await Task.Run(S.Power.HasBattery); }
        catch { IsLaptop = false; }
    }

    private void Refresh()
    {
        foreach (var item in Items) item.Refresh();
        var count = S.Backup.Entries.Count;
        ActiveChangesText = count == 0
            ? "Aucun réglage modifié par Unpark pour l'instant."
            : $"{count} réglage(s) modifié(s) par Unpark. « Tout annuler » remet l'état d'origine exact.";
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        var todo = Items.Where(i => i.IsChecked && !i.AlreadyApplied).ToList();
        if (todo.Count == 0 && !ProposeCleanup)
        {
            Dialogs.Info("Tout ce qui est coché est déjà en place.");
            return;
        }

        // Récapitulatif avant de valider.
        var lines = todo.Select(i => "• " + i.Name + (i.RequiresRestart ? " (redémarrage requis)" : "")).ToList();
        if (ProposeCleanup) lines.Add("• Puis : nettoyage proposé (tu verras l'espace récupérable avant de confirmer)");
        var laptop = IsLaptop && todo.Any(i => i.Setting.Id == S.PowerPlan.Id)
            ? "\n\nPortable : le plan Unpark chauffe davantage le PC sur secteur ; sur batterie, il garde les réglages économes du plan Équilibré."
            : "";
        var restart = todo.Any(i => i.RequiresRestart) ? "\n\nUn redémarrage sera nécessaire à la fin." : "";
        if (!Dialogs.Confirm($"Unpark va faire :\n\n{string.Join("\n", lines)}{laptop}{restart}\n\n" +
                             "Tout reste annulable avec « Tout annuler ». Continuer ?"))
            return;

        if (todo.Count > 0)
        {
            var results = await SafetyFlow.ApplyManyAsync(todo.Select(i => i.Setting).ToList());
            if (results.Count == 0) return; // abandon au moment du point de restauration
        }
        if (ProposeCleanup) await CleanupAsync();
    }

    private static async Task CleanupAsync()
    {
        var scans = await S.RunBusyAsync("Analyse des fichiers inutiles…", S.Cleanup.Scan);
        // Même sélection par défaut que la page Nettoyage : tout sauf la corbeille.
        var selected = scans.Where(s => s.Bytes > 0 && s.Target.Kind != CleanupKind.RecycleBin).ToList();
        if (selected.Count == 0)
        {
            Dialogs.Info("Rien à nettoyer : ton PC est déjà propre.");
            return;
        }
        var list = string.Join("\n", selected.Select(s => $"• {s.Target.Label} ({Format.Bytes(s.Bytes)})"));
        if (!Dialogs.Confirm($"Nettoyer maintenant ? Espace récupérable : {Format.Bytes(selected.Sum(s => s.Bytes))}\n\n{list}\n\n" +
                             "Ces fichiers ne pourront pas être récupérés (la corbeille n'est pas touchée)."))
            return;
        var results = await S.RunBusyAsync("Nettoyage en cours…", () => S.Cleanup.Clean(selected.Select(s => s.Target.Id)));
        var skipped = results.Sum(r => r.Skipped);
        Dialogs.Info($"Espace gagné : {Format.Bytes(results.Sum(r => r.FreedBytes))}." +
                     (skipped > 0 ? $"\n{skipped} fichier(s) en cours d'utilisation ignoré(s)." : ""));
    }

    [RelayCommand]
    private Task RevertAllAsync() => SafetyFlow.RevertAllAsync();
}
