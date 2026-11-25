using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Unpark.Core.Debloat;
using Unpark.Services;

namespace Unpark.ViewModels;

public sealed partial class DebloatRowViewModel(DebloatCandidate app) : ObservableObject
{
    public DebloatCandidate App { get; } = app;
    public string Name => App.DisplayName;
    public string PackageName => App.Package.Name;
    public string PolicyText => App.Policy == AppPolicy.Recommended ? "Recommandé" : "Optionnel";
    public bool Recommended => App.Policy == AppPolicy.Recommended;
    [ObservableProperty] private bool _isChecked = app.DefaultChecked;
}

public sealed partial class RemovedAppRowViewModel(RemovedApp app) : ObservableObject
{
    public string Name => app.DisplayName;
    public string When => $"Retirée le {app.RemovedUtc.ToLocalTime():dd/MM/yyyy}";

    [RelayCommand]
    private void OpenStore() => Process.Start(new ProcessStartInfo(app.StoreUrl) { UseShellExecute = true });
}

/// <summary>Page « Debloat » : applis préinstallées à retirer, avec une liste sûre cochée par défaut.</summary>
public sealed partial class DebloatViewModel : PageViewModel
{
    private static AppServices S => AppServices.Current;

    public DebloatViewModel() : base("Debloat", "")
    {
        LoadHistory();
    }

    public ObservableCollection<DebloatRowViewModel> Rows { get; } = [];
    public ObservableCollection<RemovedAppRowViewModel> History { get; } = [];
    [ObservableProperty] private bool _loaded;
    [ObservableProperty] private bool _hasHistory;
    [ObservableProperty] private string _summary = "Clique sur « Lister les applis » pour voir les applis préinstallées.";

    private void LoadHistory()
    {
        History.Clear();
        foreach (var app in S.Debloat.History().OrderByDescending(a => a.RemovedUtc)) History.Add(new RemovedAppRowViewModel(app));
        HasHistory = History.Count > 0;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            var candidates = await S.RunBusyAsync("Lecture des applis installées…", S.Debloat.Candidates);
            Rows.Clear();
            foreach (var c in candidates) Rows.Add(new DebloatRowViewModel(c));
            Loaded = true;
            Summary = $"{Rows.Count} appli(s) proposée(s), dont {Rows.Count(r => r.Recommended)} recommandée(s) (cochées). " +
                      "Le Store, la Calculatrice, Photos, le Terminal, les applis Xbox et les composants système ne sont jamais proposés.";
        }
        catch (Exception e)
        {
            S.Log.Error("Lecture des applis impossible", e);
            Dialogs.Error($"Lecture des applis impossible : {e.Message}");
        }
    }

    [RelayCommand]
    private async Task RemoveAsync()
    {
        var selected = Rows.Where(r => r.IsChecked).ToList();
        if (selected.Count == 0)
        {
            Dialogs.Info("Coche au moins une appli à retirer.");
            return;
        }
        var list = string.Join("\n", selected.Select(r => "• " + r.Name));
        if (!Dialogs.Confirm($"Retirer ces applis pour ton compte Windows ?\n\n{list}\n\n" +
                             "Pas d'« Annuler » automatique : une appli retirée se réinstalle depuis le Microsoft Store (liens gardés plus bas dans cette page).", danger: true))
            return;
        if (!await SafetyFlow.BeforeFirstChangeAsync()) return;

        var results = await S.RunBusyAsync($"Retrait de {selected.Count} appli(s)…", () => S.Debloat.Remove(selected.Select(r => r.App)));
        var failed = results.Where(r => !r.Success).ToList();
        if (failed.Count == 0) Dialogs.Info($"{results.Count} appli(s) retirée(s).");
        else Dialogs.Warn($"{results.Count - failed.Count} appli(s) retirée(s), {failed.Count} en échec :\n\n" +
                          string.Join("\n", failed.Select(f => $"• {f.App.DisplayName} : {f.Error}")));
        LoadHistory();
        await LoadAsync();
    }
}
