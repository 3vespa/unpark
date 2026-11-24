using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Unpark.Core.Gaming;
using Unpark.Services;

namespace Unpark.ViewModels;

/// <summary>Page « Gaming » : Mode Jeu, HAGS, Game DVR, souris, effets visuels.</summary>
public sealed partial class GamingViewModel : PageViewModel
{
    private static AppServices S => AppServices.Current;

    public GamingViewModel() : base("Gaming", "")
    {
        foreach (var setting in S.Gaming)
            Items.Add(new SettingItemViewModel(setting, setting.Id == GamingSettings.HagsId
                ? "Non garanti sur ce PC : sans carte et pilote compatibles, Windows ignore ce réglage."
                : setting.Id == GamingSettings.VisualFxId ? "Optionnel" : null));
        S.StateChanged += () => { foreach (var item in Items) item.Refresh(); };
    }

    public ObservableCollection<SettingItemViewModel> Items { get; }  = [];

    /// <summary>Tout le module sauf les effets visuels (optionnels).</summary>
    [RelayCommand]
    private Task ApplyAllAsync() =>
        SafetyFlow.ApplyManyAsync(S.Gaming.Where(s => s.Id != GamingSettings.VisualFxId && !s.IsApplied()).ToList());
}
