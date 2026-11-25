using System.ComponentModel;
using System.Windows;
using Unpark.Interop;
using Unpark.Services;
using Unpark.ViewModels;

namespace Unpark;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Barre de titre sombre (Windows 10 20H1+ et Windows 11), sans effet ailleurs.
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Optimisation de lecteur en cours : prévenir avant de l'arrêter.
        if (DriveOptimizationViewModel.Running is { } running)
        {
            if (!Dialogs.Confirm("Une optimisation de lecteur est en cours.\n\nQuitter Unpark l'arrête (sans risque pour tes fichiers). Quitter quand même ?"))
            {
                e.Cancel = true;
                return;
            }
            AppServices.Current.Log.Info("Fermeture d'Unpark pendant une optimisation de lecteur : arrêt demandé");
            running.Stop();
        }
        base.OnClosing(e);
    }
}
