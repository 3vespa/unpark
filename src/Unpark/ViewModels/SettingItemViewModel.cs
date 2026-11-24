using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Unpark.Core.Gaming;
using Unpark.Core.Settings;
using Unpark.Services;

namespace Unpark.ViewModels;

/// <summary>Une ligne d'aperçu (ex. un effet visuel et son état actuel).</summary>
public sealed record PreviewRow(string Label, string State);

/// <summary>Carte d'un réglage réversible : état, badges, boutons Appliquer / Annuler.</summary>
public sealed partial class SettingItemViewModel : ObservableObject
{
    private static AppServices S => AppServices.Current;

    public SettingItemViewModel(ISetting setting, string? note = null)
    {
        Setting = setting;
        Note = note;
        Refresh();
    }

    public ISetting Setting { get; }
    public string Name => Setting.Name;
    public string Description => Setting.Description;
    public bool RequiresRestart => Setting.RequiresRestart;
    /// <summary>Remarque affichée en orange (ex. « Non garanti sur ce PC »).</summary>
    public string? Note { get; }

    public ObservableCollection<PreviewRow> Preview { get; } = [];
    public bool HasPreview => Setting is VisualEffectsSetting;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _isApplied;
    [ObservableProperty] private bool _hasBackup;

    public void Refresh()
    {
        try
        {
            IsApplied = Setting.IsApplied();
            HasBackup = S.Backup.Get(Setting.Id) is not null;
            StatusText = IsApplied && HasBackup ? "Appliqué par Unpark"
                : IsApplied ? "Déjà réglé ainsi sur ce PC"
                : HasBackup ? "Modifié depuis (ré-appliquer ou annuler)"
                : "Pas appliqué";
            if (Setting is VisualEffectsSetting fx)
            {
                Preview.Clear();
                foreach (var (effect, on) in fx.Preview())
                    Preview.Add(new PreviewRow(effect.Label, on ? "activé → sera désactivé" : "déjà désactivé"));
            }
        }
        catch (Exception e)
        {
            S.Log.Error($"Lecture de l'état de « {Setting.Name} » impossible", e);
            StatusText = "État illisible";
        }
    }

    [RelayCommand]
    private Task ApplyAsync() => SafetyFlow.ApplyAsync(Setting);

    [RelayCommand]
    private Task RevertAsync() => SafetyFlow.RevertAsync(Setting);
}
