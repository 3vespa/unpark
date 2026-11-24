using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Unpark.Core;
using Unpark.Core.SystemInfo;
using Unpark.Services;

namespace Unpark.ViewModels;

public sealed record InfoLine(string Label, string Value);
public sealed record VolumeLine(string Title, string Detail, double UsedPercent);

/// <summary>Page « Infos système ».</summary>
public sealed partial class InfoViewModel : PageViewModel
{
    private static AppServices S => AppServices.Current;
    private SystemReport? _report;

    public InfoViewModel() : base("Infos système", "")
    {
        _ = RefreshAsync();
    }

    public ObservableCollection<InfoLine> Lines { get; } = [];
    public ObservableCollection<VolumeLine> Volumes { get; } = [];
    [ObservableProperty] private string _status = "Lecture des infos…";

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            Status = "Lecture des infos…";
            _report = await Task.Run(S.SystemInfo.Collect);
            var r = _report;
            Lines.Clear();
            Lines.Add(new("Windows", r.Windows.ToString()));
            if (r.Cpu is { } c) Lines.Add(new("Processeur", $"{c.Name}\n{c.Cores} cœurs, {c.Threads} threads, {c.MaxMhz} MHz"));
            foreach (var g in r.Gpus)
                Lines.Add(new("Carte graphique", $"{g.Name}{(g.VramBytes is { } v ? $" — {Format.Bytes(v)}" : "")}\nPilote {g.Driver}"));
            if (r.Ram is { } m) Lines.Add(new("Mémoire", $"{Format.Bytes(m.TotalBytes)} — {m.Modules} barrette(s){(m.SpeedMhz > 0 ? $", {m.SpeedMhz} MHz" : "")}"));
            foreach (var d in r.Disks) Lines.Add(new("Disque", $"{d.Model}\n{d.Type}, {Format.Bytes(d.SizeBytes)}"));
            Lines.Add(new("Plan d'alimentation", r.ActivePowerPlan));
            Lines.Add(new("Démarrage", $"{r.StartupEnabled} programme(s) activé(s) sur {r.StartupTotal}"));
            Volumes.Clear();
            foreach (var v in r.Volumes)
                Volumes.Add(new($"Lecteur {v.Letter}{(string.IsNullOrWhiteSpace(v.Label) ? "" : $" ({v.Label})")}",
                    $"{Format.Bytes(v.FreeBytes)} libres sur {Format.Bytes(v.TotalBytes)}", v.UsedRatio * 100));
            Status = "";
        }
        catch (Exception e)
        {
            S.Log.Error("Lecture des infos système impossible", e);
            Status = $"Lecture impossible : {e.Message}";
        }
    }

    [RelayCommand]
    private void Copy()
    {
        if (_report is null) return;
        try
        {
            Clipboard.SetText(_report.ToText());
            Dialogs.Info("Résumé copié : tu peux le coller sur Discord (Ctrl+V).");
        }
        catch (Exception e)
        {
            // Presse-papiers occupé par un autre programme : on réessaie plus tard.
            Dialogs.Warn($"Copie impossible pour l'instant ({e.Message}). Réessaie dans un instant.");
        }
    }
}
