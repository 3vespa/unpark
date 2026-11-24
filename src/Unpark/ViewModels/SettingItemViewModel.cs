using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Unpark.Core.Gaming;
using Unpark.Core.Settings;
using Unpark.Services;

namespace Unpark.ViewModels;

/// <summary>Carte d'un réglage réversible : état, badges, boutons Appliquer / Annuler.</summary>
public sealed partial class SettingItemViewModel : ObservableObject
{
    private static AppServices S => AppServices.Current;

    public SettingItemViewModel(ISetting setting, string? note = null)
    {
        Setting = setting;
        _note = note;
        Refresh();
    }

    public ISetting Setting { get; }
    public string Name => Setting.Name;
    public string Description => Setting.Description;
    public bool RequiresRestart => Setting.RequiresRestart;
    /// <summary>Remarque affichée en orange (ex. conseil HAGS selon la carte graphique).</summary>
    [ObservableProperty] private string? _note;

    /// <summary>Raison si le réglage n'existe pas sur ce PC (version de Windows, verrou…), sinon null.</summary>
    [ObservableProperty] private string? _unavailableReason;
    public bool IsAvailable => UnavailableReason is null;
    partial void OnUnavailableReasonChanged(string? value) => OnPropertyChanged(nameof(IsAvailable));

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
            UnavailableReason = (Setting as IConditionalSetting)?.UnavailableReason();
            if (UnavailableReason is not null && !HasBackup) StatusText = "Indisponible sur ce PC";
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
