using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Unpark.Core;
using Unpark.Core.Cleanup;
using Unpark.Services;

namespace Unpark.ViewModels;

public sealed partial class CleanupRowViewModel : ObservableObject
{
    public CleanupRowViewModel(CleanupScan scan, Action changed)
    {
        Target = scan.Target;
        Bytes = scan.Bytes;
        SizeText = scan.Bytes == 0 ? "Rien à nettoyer" : $"{Format.Bytes(scan.Bytes)} ({scan.Files} élément(s))";
        // La corbeille peut contenir des fichiers que tu veux récupérer : décochée par défaut.
        _isChecked = scan.Bytes > 0 && Target.Kind != CleanupKind.RecycleBin;
        _changed = changed;
    }

    private readonly Action _changed;
    public CleanupTarget Target { get; }
    public long Bytes { get; }
    public string Label => Target.Label;
    public string Description => Target.Description;
    public string SizeText { get; }

    [ObservableProperty] private bool _isChecked;
    partial void OnIsCheckedChanged(bool value) => _changed();
}

/// <summary>Page « Nettoyage » : analyse (espace récupérable), puis nettoyage de la sélection (espace gagné).</summary>
public sealed partial class CleanupViewModel : PageViewModel
{
    private static AppServices S => AppServices.Current;

    public CleanupViewModel() : base("Nettoyage", "") { }

    public ObservableCollection<CleanupRowViewModel> Rows { get; } = [];
    [ObservableProperty] private string _totalText = "Lance l'analyse pour voir l'espace récupérable.";
    [ObservableProperty] private string? _resultText;
    [ObservableProperty] private bool _scanned;

    private void UpdateTotal()
    {
        var selected = Rows.Where(r => r.IsChecked).Sum(r => r.Bytes);
        TotalText = $"Espace récupérable sélectionné : {Format.Bytes(selected)} (sur {Format.Bytes(Rows.Sum(r => r.Bytes))} au total)";
    }

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        var scans = await S.RunBusyAsync("Analyse des fichiers inutiles…", S.Cleanup.Scan);
        Rows.Clear();
        foreach (var scan in scans) Rows.Add(new CleanupRowViewModel(scan, UpdateTotal));
        Scanned = true;
        ResultText = null;
        UpdateTotal();
    }

    [RelayCommand]
    private async Task CleanAsync()
    {
        var selected = Rows.Where(r => r.IsChecked).ToList();
        if (selected.Count == 0)
        {
            Dialogs.Info("Coche au moins un élément à nettoyer.");
            return;
        }
        var list = string.Join("\n", selected.Select(r => $"• {r.Label} ({Format.Bytes(r.Bytes)})"));
        if (!Dialogs.Confirm($"Supprimer définitivement :\n\n{list}\n\nCes fichiers ne pourront pas être récupérés. Les fichiers en cours d'utilisation seront simplement ignorés.\n\nContinuer ?", danger: true))
            return;

        var results = await S.RunBusyAsync("Nettoyage en cours…", () => S.Cleanup.Clean(selected.Select(r => r.Target.Id)));
        var freed = results.Sum(r => r.FreedBytes);
        var skipped = results.Sum(r => r.Skipped);
        var errors = results.Where(r => r.Error is not null).Select(r => "• " + r.Error).ToList();
        ResultText = $"Espace gagné : {Format.Bytes(freed)}." +
                     (skipped > 0 ? $" {skipped} fichier(s) en cours d'utilisation ignoré(s)." : "") +
                     (errors.Count > 0 ? "\n" + string.Join("\n", errors) : "");
        Dialogs.Info(ResultText);
        await AnalyzeAsync();
        ResultText = $"Espace gagné : {Format.Bytes(freed)}." + (skipped > 0 ? $" {skipped} fichier(s) en cours d'utilisation ignoré(s)." : "");
    }
}
