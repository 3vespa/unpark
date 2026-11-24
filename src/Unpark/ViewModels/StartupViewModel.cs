using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Unpark.Core.Startup;
using Unpark.Services;

namespace Unpark.ViewModels;

public sealed partial class StartupRowViewModel(StartupEntry entry, Func<StartupEntry, Task> toggle) : ObservableObject
{
    public StartupEntry Entry { get; } = entry;
    public string Name => Entry.DisplayName;
    public string Source => Entry.SourceText;
    public string Command => Entry.Command;
    public bool Enabled => Entry.Enabled;
    public string StateText => Entry.Enabled ? "Activé" : "Désactivé";
    public string ButtonText => Entry.Enabled ? "Désactiver" : "Activer";

    [RelayCommand]
    private Task ToggleAsync() => toggle(Entry);
}

/// <summary>Page « Démarrage » : programmes lancés avec Windows, à activer ou désactiver sans les supprimer.</summary>
public sealed partial class StartupViewModel : PageViewModel
{
    private static AppServices S => AppServices.Current;

    public StartupViewModel() : base("Démarrage", "")
    {
        S.StateChanged += () => _ = RefreshAsync();
        _ = RefreshAsync();
    }

    public ObservableCollection<StartupRowViewModel> Rows { get; } = [];
    [ObservableProperty] private string _summary = "Lecture…";

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            var entries = await Task.Run(S.Startup.List);
            Rows.Clear();
            foreach (var e in entries) Rows.Add(new StartupRowViewModel(e, ToggleAsync));
            var enabled = entries.Count(e => e.Enabled);
            Summary = $"{entries.Count} programme(s) trouvé(s), dont {enabled} activé(s) au démarrage.";
        }
        catch (Exception e)
        {
            S.Log.Error("Lecture des programmes au démarrage impossible", e);
            Summary = $"Lecture impossible : {e.Message}";
        }
    }

    private static async Task ToggleAsync(StartupEntry entry)
    {
        var (setting, revert) = S.Startup.Toggle(entry, enable: !entry.Enabled);
        if (revert) await SafetyFlow.RevertAsync(setting);
        else await SafetyFlow.ApplyAsync(setting);
    }
}
