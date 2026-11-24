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
            Items.Add(new SettingItemViewModel(setting, setting.Id == GamingSettings.VisualFxId ? "Optionnel" : null));
        S.StateChanged += () => { foreach (var item in Items) item.Refresh(); };
        _ = LoadHagsAdviceAsync();
    }

    private bool _hagsRecommended;

    private async Task LoadHagsAdviceAsync()
    {
        var advice = await S.HagsAdviceAsync();
        _hagsRecommended = advice.Recommended;
        Items.Single(i => i.Setting.Id == GamingSettings.HagsId).Note = advice.Note;
    }

    public ObservableCollection<SettingItemViewModel> Items { get; }  = [];

    /// <summary>Tout le module sauf les effets visuels (optionnels) ; HAGS seulement s'il est recommandé pour la carte.</summary>
    [RelayCommand]
    private Task ApplyAllAsync() =>
        SafetyFlow.ApplyManyAsync(S.Gaming
            .Where(s => s.Id != GamingSettings.VisualFxId && (s.Id != GamingSettings.HagsId || _hagsRecommended) && !s.IsApplied())
            .ToList());
}
