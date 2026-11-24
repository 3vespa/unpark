using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Unpark.Core;

namespace Unpark.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public MainViewModel()
    {
        Pages =
        [
            new HomeViewModel(),
            new PlaceholderViewModel("Gaming en 1 clic", "", "Plan Unpark Performance + réglages Gaming + nettoyage, avec un récapitulatif avant de valider et un bouton « Tout annuler »."),
            new PlaceholderViewModel("Plan d'alimentation", "", "Plan « Unpark Performance » : cœurs toujours actifs, processeur à 100 %, boost agressif. Retour à ton ancien plan en un clic."),
            new PlaceholderViewModel("Gaming", "", "Mode Jeu, planification GPU (HAGS), captures Game DVR, accélération de la souris, effets visuels."),
            new PlaceholderViewModel("Démarrage", "", "Programmes lancés au démarrage de Windows, à activer ou désactiver comme dans le Gestionnaire des tâches."),
            new PlaceholderViewModel("Nettoyage", "", "Fichiers temporaires, cache de Windows Update, corbeille, miniatures. Espace récupérable affiché avant."),
            new PlaceholderViewModel("Debloat", "", "Applis préinstallées à retirer, avec une liste sûre cochée par défaut."),
            new PlaceholderViewModel("Infos système", "", "Processeur, carte graphique, mémoire, disques, version de Windows, plan d'alimentation actif."),
        ];
        _selectedPage = Pages[0];
    }

    public string AppName => AppInfo.Name;
    public string Subtitle => AppInfo.Subtitle;
    public string Version => $"Version {AppInfo.Version}";
    public string WindowTitle => $"{AppInfo.Name} {AppInfo.DisplayVersion} — {AppInfo.Subtitle}";

    public ObservableCollection<PageViewModel> Pages { get; }

    [ObservableProperty]
    private PageViewModel _selectedPage;
}
