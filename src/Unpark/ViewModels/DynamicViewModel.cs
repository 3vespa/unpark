using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Unpark.Core.Dynamic;
using Unpark.Services;
using Unpark.Tray;

namespace Unpark.ViewModels;

/// <summary>Page « Mode dynamique » : réglages, démarrer / arrêter l'icône, lancement avec Windows.</summary>
public sealed partial class DynamicViewModel : PageViewModel
{
    private static AppServices S => AppServices.Current;
    private bool _loading;

    public DynamicViewModel() : base("Mode dynamique", "")
    {
        _loading = true;
        var settings = S.DynamicStore.Load();
        _enabled = settings.Enabled;
        _idleMinutes = DynamicSettings.IdleChoices.Contains(settings.IdleMinutes) ? settings.IdleMinutes : 10;
        _loading = false;
        Autostart = new SettingItemViewModel(S.DynamicAutostart);
        S.StateChanged += Autostart.Refresh;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) => RefreshStatus();
        timer.Start();
        RefreshStatus();
    }

    public int[] IdleChoices => DynamicSettings.IdleChoices;
    public SettingItemViewModel Autostart { get; }

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private int _idleMinutes;
    [ObservableProperty] private bool _running;
    [ObservableProperty] private string _statusText = "";

    partial void OnEnabledChanged(bool value) => Save();
    partial void OnIdleMinutesChanged(int value) => Save();

    private void Save()
    {
        if (_loading) return;
        try
        {
            S.DynamicStore.Save(new DynamicSettings(Enabled, IdleMinutes));
            S.Log.Info($"Mode dynamique : réglages {(Enabled ? "activé" : "désactivé")}, Équilibré après {IdleMinutes} min d'inactivité");
        }
        catch (Exception e)
        {
            S.Log.Error("Enregistrement des réglages du mode dynamique impossible", e);
        }
    }

    private void RefreshStatus()
    {
        Running = TrayHost.IsRunning();
        StatusText = Running
            ? "Le mode dynamique tourne : icône « U » dans la zone de notification (vert = Unpark Performance, gris = Équilibré, orange = en pause)."
            : "Le mode dynamique n'est pas lancé.";
    }

    [RelayCommand]
    private void Start()
    {
        if (!S.PowerPlan.IsInstalled())
        {
            if (Dialogs.Confirm("Le plan Unpark Performance n'est pas installé : le mode dynamique n'aurait rien à activer.\n\nOuvrir la page Plan d'alimentation ?"))
                S.Navigate(typeof(PowerPlanViewModel));
            return;
        }
        if (TrayHost.IsRunning()) return;
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--tray") { UseShellExecute = false });
            S.Log.Info("Mode dynamique : lancé depuis l'appli");
        }
        catch (Exception e)
        {
            S.Log.Error("Lancement du mode dynamique impossible", e);
            Dialogs.Error($"Lancement impossible : {e.Message}");
        }
        RefreshStatus();
    }

    [RelayCommand]
    private void Stop()
    {
        if (!TrayHost.RequestQuit()) Dialogs.Info("Le mode dynamique n'est pas lancé.");
        RefreshStatus();
    }
}
