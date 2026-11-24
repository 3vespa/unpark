using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Unpark.Core.Power;
using Unpark.Services;

namespace Unpark.ViewModels;

/// <summary>Une ligne du tableau des réglages du plan.</summary>
public sealed record PowerSettingRow(string Label, string Target, string Current, bool Ok, bool Installed)
{
    public string StateIcon => !Installed ? "" : Ok ? "" : "";
}

/// <summary>Page « Plan d'alimentation » : installer / revenir à l'ancien plan / supprimer le plan Unpark.</summary>
public sealed partial class PowerPlanViewModel : PageViewModel
{
    private static AppServices S => AppServices.Current;

    public PowerPlanViewModel() : base("Plan d'alimentation", "")
    {
        S.StateChanged += () => _ = RefreshAsync();
        _ = RefreshAsync();
    }

    public ObservableCollection<PowerSettingRow> Rows { get; } = [];

    [ObservableProperty] private string _activeText = "Lecture…";
    [ObservableProperty] private string _unparkText = "";
    [ObservableProperty] private string _previousText = "";
    [ObservableProperty] private bool _isLaptop;
    [ObservableProperty] private bool _installed;
    [ObservableProperty] private bool _unparkActive;
    [ObservableProperty] private string? _error;

    private async Task RefreshAsync()
    {
        try
        {
            var status = await Task.Run(S.PowerPlan.GetStatus);
            ActiveText = $"Plan actif : {status.ActiveName}";
            Installed = status.UnparkInstalled;
            UnparkActive = status.UnparkActive;
            UnparkText = status.UnparkActive ? "Unpark Performance est installé et actif."
                : status.UnparkInstalled ? "Unpark Performance est installé mais pas actif."
                : "Unpark Performance n'est pas installé.";
            PreviousText = status.PreviousName is { } p ? $"Ton ancien plan mémorisé : {p}" : "";
            IsLaptop = status.IsLaptop;
            Rows.Clear();
            foreach (var s in status.Settings)
                Rows.Add(new PowerSettingRow(s.Def.Label, s.Def.ValueText, status.UnparkInstalled ? s.Def.Format(s.CurrentAc) : "—", s.Ok, status.UnparkInstalled));
            Error = null;
        }
        catch (Exception e)
        {
            S.Log.Error("Lecture de l'état de l'alimentation impossible", e);
            Error = $"Impossible de lire l'état de l'alimentation : {e.Message}";
        }
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (IsLaptop && !Dialogs.Confirm(
                "Ton PC est un portable.\n\nSur secteur, Unpark Performance garde le processeur à fond : le PC chauffera davantage et les ventilateurs tourneront plus. " +
                "Sur batterie, le plan garde les réglages économes du plan Équilibré, pour ne pas vider la batterie.\n\nContinuer ?"))
            return;
        await SafetyFlow.ApplyAsync(S.PowerPlan);
        if (S.PowerPlan.IsApplied()) Dialogs.Info("Unpark Performance est installé et actif. Pas besoin de redémarrer.");
    }

    [RelayCommand]
    private async Task SwitchBackAsync()
    {
        try
        {
            var name = await S.RunBusyAsync("Retour à ton ancien plan…", S.PowerPlan.SwitchToPrevious);
            Dialogs.Info($"Plan « {name} » réactivé. Unpark Performance reste installé : tu peux le réactiver quand tu veux.");
        }
        catch (Exception e)
        {
            S.Log.Error("Retour à l'ancien plan impossible", e);
            Dialogs.Error($"Retour à l'ancien plan impossible : {e.Message}");
        }
        S.NotifyChanged();
    }

    [RelayCommand]
    private async Task RemoveAsync()
    {
        if (!Dialogs.Confirm("Supprimer le plan Unpark Performance ?\n\nTon ancien plan sera réactivé et les paramètres rendus visibles seront de nouveau cachés.")) return;
        await SafetyFlow.RevertAsync(S.PowerPlan);
        if (!S.PowerPlan.IsInstalled()) Dialogs.Info("Plan Unpark Performance supprimé. Ton ancien plan est actif.");
    }
}
