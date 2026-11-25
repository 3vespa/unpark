using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using Unpark.Services;

namespace Unpark;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Interface en français (formats de nombres, dates…).
        var fr = new CultureInfo("fr-FR");
        CultureInfo.DefaultThreadCurrentCulture = fr;
        CultureInfo.DefaultThreadCurrentUICulture = fr;
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement), new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(fr.IetfLanguageTag)));

        AppServices.Init();

        // Une erreur inattendue ne doit jamais fermer l'appli sans explication.
        DispatcherUnhandledException += OnUnhandledException;
        base.OnStartup(e);

        if (e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase))
        {
            // Lancement par la tâche du mode dynamique d'une ancienne version : ce mode n'existe plus, on se ferme sans rien afficher.
            AppServices.Current.Log.Info("Lancé avec --tray (mode dynamique d'une ancienne version, retiré en v0.9.1) : fermeture");
            Shutdown();
            return;
        }

        new MainWindow().Show();
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppServices.Current?.Log.Error("Erreur inattendue", e.Exception);
        MessageBox.Show(
            $"Une erreur inattendue est survenue :\n\n{e.Exception.Message}\n\nLes détails sont dans le journal (Accueil → Ouvrir les journaux).",
            "Unpark", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
