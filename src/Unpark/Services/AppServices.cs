using System.Security.Principal;
using Unpark.Core;
using Unpark.Core.Backup;
using Unpark.Core.Cleanup;
using Unpark.Core.Commands;
using Unpark.Core.Debloat;
using Unpark.Core.Diagnostics;
using Unpark.Core.Disks;
using Unpark.Core.Display;
using Unpark.Core.Gaming;
using Unpark.Core.Legacy;
using Unpark.Core.Logging;
using Unpark.Core.Power;
using Unpark.Core.Registry;
using Unpark.Core.RestorePoints;
using Unpark.Core.Settings;
using Unpark.Core.Startup;
using Unpark.Core.SystemInfo;
using Unpark.Core.Updates;
using Unpark.Interop;

namespace Unpark.Services;

/// <summary>Assemble les services de l'appli (créés une seule fois au démarrage).</summary>
public sealed class AppServices
{
    public static AppServices Current { get; private set; } = null!;

    private AppServices()
    {
        Paths = AppPaths.Default;
        Log = new ActionLog(Paths.LogsDir);
        Backup = new BackupStore(Paths.BackupFile, Log);
        Registry = new WindowsRegistry();
        Context = new SettingContext(Registry, Backup, Log);
        Commands = new ProcessCommandRunner();
        Power = new WindowsPowerApi();
        PowerPlan = new UnparkPowerPlan(Context, Power, Commands);
        var systemUi = new WindowsSystemUiApi();
        Gaming = GamingSettings.Create(Context, systemUi, WindowsVersionInfo.Read(Registry).Build);
        var legacy = GamingSettings.Legacy(Context, systemUi);
        Cleanup = new CleanupService(new WindowsCleanupSystem(), Log, CleanupPaths.Default);
        Debloat = new DebloatService(new WindowsAppxApi(), Log, Paths.DebloatHistoryFile);
        Startup = new StartupService(Context, new WindowsLogonTaskApi(), StartupFolders.Default);
        // Mode dynamique (retiré en v0.9.1) : seulement pour que « Tout annuler » défasse ce qu'une ancienne version avait fait.
        var legacyDynamic = new LegacyDynamicAutostart(Context, new WindowsLegacyDynamicSystem(System.IO.Path.Combine(Paths.Root, "dynamic.json")));
        Disks = new DiskOptimizer(new WindowsDiskSystem(), Log);
        ScheduledOptimization = new ScheduledOptimizationSetting(Context, new WindowsScheduledOptimizationApi());
        RefreshRate = new RefreshRateChanger(Context, new WindowsDisplayModeApi());
        Engine = new SettingsEngine(Context, [PowerPlan, .. Gaming, .. legacy, ScheduledOptimization], SettingsEngine.SystemLastBootUtc,
            id => id == LegacyDynamicAutostart.SettingId ? legacyDynamic : RefreshRate.Resolve(id) ?? Startup.Resolve(id));
        Preferences = new PreferencesStore(Paths.PreferencesFile, Log);
        Updates = new UpdateService(new HttpUpdateClient(AppInfo.Version), Log, Paths.UpdatesDir, AppInfo.Version);
        UpdateInstaller = new UpdateInstaller(Log);
        SystemInfo = new SystemInfoService(new WmiHardwareInfoApi(), Registry, Power, Startup);
        Diagnostics = new WindowsDiagnostics(Registry, Power, Startup, Commands, Log);
        _hags = new Lazy<Task<HagsAdvice>>(() => Task.Run(() =>
        {
            try { return HagsAdvice.For(new WmiHardwareInfoApi().Gpus().Select(g => g.Name)); }
            catch { return HagsAdvice.For([]); }
        }));
        RestorePoints = new RestorePointService(new WmiRestorePointApi(), Registry, Log);
    }

    public AppPaths Paths { get; }
    public ActionLog Log { get; }
    public BackupStore Backup { get; }
    public IRegistry Registry { get; }
    public SettingContext Context { get; }
    public SettingsEngine Engine { get; }
    public RestorePointService RestorePoints { get; }
    public ICommandRunner Commands { get; }
    public IPowerApi Power { get; }
    public UnparkPowerPlan PowerPlan { get; }
    public IReadOnlyList<ISetting> Gaming { get; }
    public StartupService Startup { get; }
    public CleanupService Cleanup { get; }
    public DiskOptimizer Disks { get; }
    public ScheduledOptimizationSetting ScheduledOptimization { get; }
    public DebloatService Debloat { get; }
    public SystemInfoService SystemInfo { get; }
    public WindowsDiagnostics Diagnostics { get; }
    public RefreshRateChanger RefreshRate { get; }
    public PreferencesStore Preferences { get; }
    public UpdateService Updates { get; }
    public UpdateInstaller UpdateInstaller { get; }

    private readonly Lazy<Task<HagsAdvice>> _hags;
    /// <summary>Conseil HAGS selon la carte graphique (lu une seule fois).</summary>
    public Task<HagsAdvice> HagsAdviceAsync() => _hags.Value;

    /// <summary>Demande d'ouvrir une page de l'appli (ex. bouton « Corriger » du diagnostic).</summary>
    public event Action<Type>? NavigateRequested;
    public void Navigate(Type pageViewModel) => NavigateRequested?.Invoke(pageViewModel);

    /// <summary>Prévient les écrans qu'un réglage a changé (bandeau de redémarrage, compteurs…).</summary>
    public event Action? StateChanged;
    public void NotifyChanged() => StateChanged?.Invoke();

    /// <summary>Opération longue en cours (texte affiché par-dessus l'appli), null sinon.</summary>
    public event Action<string?>? BusyChanged;

    private int _busyCount;
    /// <summary>Une opération longue est en cours (application de réglages, nettoyage, diagnostic…) : jamais de redémarrage d'Unpark pendant ce temps.</summary>
    public bool IsBusy => Volatile.Read(ref _busyCount) > 0;

    /// <summary>Opération longue avec un texte d'avancement qui change.</summary>
    public async Task<T> RunBusyAsync<T>(string text, Func<IProgress<string>, Task<T>> work)
    {
        Interlocked.Increment(ref _busyCount);
        BusyChanged?.Invoke(text);
        var progress = new Progress<string>(t => BusyChanged?.Invoke(t));
        try { return await Task.Run(() => work(progress)); }
        finally
        {
            Interlocked.Decrement(ref _busyCount);
            BusyChanged?.Invoke(null);
        }
    }

    public async Task<T> RunBusyAsync<T>(string text, Func<T> work)
    {
        Interlocked.Increment(ref _busyCount);
        BusyChanged?.Invoke(text);
        try { return await Task.Run(work); }
        finally
        {
            Interlocked.Decrement(ref _busyCount);
            BusyChanged?.Invoke(null);
        }
    }

    public static void Init()
    {
        Current = new AppServices();
        var admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        Current.Log.Info($"Démarrage {AppInfo.Name} {AppInfo.DisplayVersion} — Windows {Environment.OSVersion.Version}, administrateur : {(admin ? "oui" : "non")}");
    }
}
