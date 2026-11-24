using System.Security.Principal;
using Unpark.Core;
using Unpark.Core.Backup;
using Unpark.Core.Logging;
using Unpark.Core.Registry;
using Unpark.Core.RestorePoints;
using Unpark.Core.Settings;

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
        Demo = new DemoSetting(Context);
        Engine = new SettingsEngine(Context, [Demo], SettingsEngine.SystemLastBootUtc);
        RestorePoints = new RestorePointService(new WmiRestorePointApi(), Registry, Log);
    }

    public AppPaths Paths { get; }
    public ActionLog Log { get; }
    public BackupStore Backup { get; }
    public IRegistry Registry { get; }
    public SettingContext Context { get; }
    public SettingsEngine Engine { get; }
    public RestorePointService RestorePoints { get; }
    public DemoSetting Demo { get; }

    /// <summary>Prévient les écrans qu'un réglage a changé (bandeau de redémarrage, compteurs…).</summary>
    public event Action? StateChanged;
    public void NotifyChanged() => StateChanged?.Invoke();

    /// <summary>Opération longue en cours (texte affiché par-dessus l'appli), null sinon.</summary>
    public event Action<string?>? BusyChanged;

    public async Task<T> RunBusyAsync<T>(string text, Func<T> work)
    {
        BusyChanged?.Invoke(text);
        try { return await Task.Run(work); }
        finally { BusyChanged?.Invoke(null); }
    }

    public static void Init()
    {
        Current = new AppServices();
        var admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        Current.Log.Info($"Démarrage {AppInfo.Name} {AppInfo.DisplayVersion} — Windows {Environment.OSVersion.Version}, administrateur : {(admin ? "oui" : "non")}");
    }
}
