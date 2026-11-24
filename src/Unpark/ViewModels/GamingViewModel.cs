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
            Items.Add(new SettingItemViewModel(setting, setting.Id switch
            {
                GamingSettings.MouseId => "Optionnel : confort de visée, pas de gain de performances.",
                GamingSettings.MemoryIntegrityId => "Optionnel : compromis entre sécurité et FPS. Un avertissement s'affiche avant.",
                _ => null,
            }));
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

    /// <summary>
    /// Réglages recommandés : Mode Jeu, enregistrement en arrière-plan, jeux en fenêtre (si disponible),
    /// HAGS seulement sur RTX 40/50. Jamais la souris (confort) ni l'Intégrité de la mémoire (sécurité).
    /// </summary>
    [RelayCommand]
    private Task ApplyAllAsync() =>
        SafetyFlow.ApplyManyAsync(S.Gaming
            .Where(s => s.Id is GamingSettings.GameModeId or GamingSettings.GameDvrId or GamingSettings.WindowedGamesId
                        || (s.Id == GamingSettings.HagsId && _hagsRecommended))
            .Where(s => s is not IConditionalSetting c || c.UnavailableReason() is null)
            .Where(s => !s.IsApplied())
            .ToList());
}
