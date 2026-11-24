using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using Unpark.Services;
using Unpark.Tray;

namespace Unpark;

public partial class App : Application
{
    private Mutex? _trayMutex;
    private TrayHost? _tray;

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
            // Mode dynamique : pas de fenêtre, seulement l'icône. Une seule instance à la fois.
            _trayMutex = new Mutex(true, TrayHost.MutexName, out var first);
            if (!first)
            {
                Shutdown();
                return;
            }
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _tray = new TrayHost();
            return;
        }

        new MainWindow().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _trayMutex?.Dispose();
        base.OnExit(e);
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
