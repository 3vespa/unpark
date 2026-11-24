using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Unpark.Core;
using Unpark.Services;

namespace Unpark.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public MainViewModel()
    {
        Pages =
        [
            new HomeViewModel(),
            new PlaceholderViewModel("Gaming en 1 clic", "", "Plan Unpark Performance + réglages Gaming + nettoyage, avec un récapitulatif avant de valider et un bouton « Tout annuler »."),
            new PowerPlanViewModel(),
            new GamingViewModel(),
            new StartupViewModel(),
            new PlaceholderViewModel("Nettoyage", "", "Fichiers temporaires, cache de Windows Update, corbeille, miniatures. Espace récupérable affiché avant."),
            new PlaceholderViewModel("Debloat", "", "Applis préinstallées à retirer, avec une liste sûre cochée par défaut."),
            new PlaceholderViewModel("Infos système", "", "Processeur, carte graphique, mémoire, disques, version de Windows, plan d'alimentation actif."),
        ];
        _selectedPage = Pages[0];

        var s = AppServices.Current;
        s.StateChanged += RefreshRestart;
        s.BusyChanged += text => System.Windows.Application.Current.Dispatcher.Invoke(() => BusyText = text);
        RefreshRestart();
    }

    /// <summary>Texte de l'opération longue en cours (affiché par-dessus l'appli), null sinon.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private string? _busyText;
    public bool IsBusy => BusyText is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RestartNeeded))]
    private string _restartText = "";
    public bool RestartNeeded => RestartText.Length > 0;

    private void RefreshRestart()
    {
        var names = AppServices.Current.Engine.PendingRestartNames();
        RestartText = names.Count == 0 ? "" : $"Redémarrage nécessaire pour : {string.Join(", ", names)}";
    }

    [RelayCommand]
    private void RestartNow()
    {
        if (!Dialogs.Confirm("Redémarrer le PC maintenant ?\n\nEnregistre ton travail et ferme tes jeux avant. Le redémarrage commence dans 10 secondes.")) return;
        AppServices.Current.Log.Info("Redémarrage demandé par l'utilisateur");
        Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 10 /c \"Redémarrage demandé par Unpark\"") { UseShellExecute = false, CreateNoWindow = true });
    }

    public string AppName => AppInfo.Name;
    public string Subtitle => AppInfo.Subtitle;
    public string Version => $"Version {AppInfo.Version}";
    public string WindowTitle => $"{AppInfo.Name} {AppInfo.DisplayVersion} — {AppInfo.Subtitle}";

    public ObservableCollection<PageViewModel> Pages { get; }

    [ObservableProperty]
    private PageViewModel _selectedPage;
}
