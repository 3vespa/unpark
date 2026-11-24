using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Unpark.Core.Backup;
using Unpark.Core.Settings;

namespace Unpark.Core.Dynamic;

/// <summary>Activité via les API documentées GetLastInputInfo et SHQueryUserNotificationState.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsActivityProbe : IActivityProbe
{
    public TimeSpan IdleTime()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info)) return TimeSpan.Zero;
        var idleMs = unchecked((uint)Environment.TickCount - info.Time); // gère le retour à zéro du compteur (49 jours)
        return TimeSpan.FromMilliseconds(idleMs);
    }

    public bool FullscreenAppRunning()
    {
        // 2 = appli plein écran (QUNS_BUSY), 3 = jeu Direct3D plein écran, 4 = mode présentation.
        return SHQueryUserNotificationState(out var state) == 0 && state is 2 or 3 or 4;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);
}

/// <summary>Tâche planifiée qui lance le mode dynamique à l'ouverture de session. Remplacé par une fausse version dans les tests.</summary>
public interface IAutostartTask
{
    /// <summary>Programme lancé par la tâche, ou null si la tâche n'existe pas.</summary>
    string? RegisteredPath();
    void Register(string exePath);
    void Delete();
}

/// <summary>
/// Tâche « \Unpark\Mode dynamique » : à l'ouverture de session, droits les plus élevés (pas de fenêtre de confirmation),
/// seulement quand l'utilisateur est connecté (aucun mot de passe enregistré), sur secteur comme sur batterie.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsAutostartTask : IAutostartTask
{
    private const string FolderName = "Unpark";
    private const string TaskName = "Mode dynamique";
    private const int TriggerLogon = 9, ActionExec = 0, RunLevelHighest = 1, LogonInteractiveToken = 3, CreateOrUpdate = 6, InstancesIgnoreNew = 2;

    private static string User => $"{Environment.UserDomainName}\\{Environment.UserName}";

    public string? RegisteredPath()
    {
        var service = Connect();
        try
        {
            dynamic task = service.GetFolder("\\" + FolderName).GetTask(TaskName);
            foreach (dynamic action in task.Definition.Actions)
                if ((int)action.Type == ActionExec) return (string)action.Path;
            return null;
        }
        catch (COMException) { return null; }
        catch (FileNotFoundException) { return null; }
        finally { Marshal.FinalReleaseComObject(service); }
    }

    public void Register(string exePath)
    {
        var service = Connect();
        try
        {
            dynamic root = service.GetFolder("\\");
            dynamic folder;
            try { folder = service.GetFolder("\\" + FolderName); }
            catch (Exception e) when (e is COMException or FileNotFoundException) { folder = root.CreateFolder(FolderName); }

            dynamic def = service.NewTask(0);
            def.RegistrationInfo.Description = "Unpark : bascule entre Unpark Performance et Équilibré selon l'activité.";
            def.Principal.UserId = User;
            def.Principal.LogonType = LogonInteractiveToken;
            def.Principal.RunLevel = RunLevelHighest;
            def.Settings.DisallowStartIfOnBatteries = false;
            def.Settings.StopIfGoingOnBatteries = false;
            def.Settings.ExecutionTimeLimit = "PT0S";
            def.Settings.MultipleInstances = InstancesIgnoreNew;
            dynamic trigger = def.Triggers.Create(TriggerLogon);
            trigger.UserId = User;
            trigger.Delay = "PT20S";
            dynamic action = def.Actions.Create(ActionExec);
            action.Path = exePath;
            action.Arguments = "--tray";
            folder.RegisterTaskDefinition(TaskName, def, CreateOrUpdate, User, null, LogonInteractiveToken, null);
        }
        finally { Marshal.FinalReleaseComObject(service); }
    }

    public void Delete()
    {
        var service = Connect();
        try
        {
            try { service.GetFolder("\\" + FolderName).DeleteTask(TaskName, 0); }
            catch (Exception e) when (e is COMException or FileNotFoundException) { }
            try { service.GetFolder("\\").DeleteFolder(FolderName, 0); } // seulement s'il est vide
            catch (Exception e) when (e is COMException or FileNotFoundException or IOException) { }
        }
        finally { Marshal.FinalReleaseComObject(service); }
    }

    private static dynamic Connect()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service") ?? throw new InvalidOperationException("Planificateur de tâches indisponible.");
        dynamic service = Activator.CreateInstance(type)!;
        service.Connect();
        return service;
    }
}

/// <summary>« Lancer le mode dynamique avec Windows » : réglage réversible (la tâche est supprimée à l'annulation).</summary>
public sealed class DynamicAutostartSetting(SettingContext ctx, IAutostartTask task, Func<string> exePath) : ISetting
{
    private const string ExistedKey = "text:dynamic.autostartExisted";

    public string Id => "dynamic.autostart";
    public string Name => "Lancer le mode dynamique avec Windows";
    public string Description => "Crée une tâche planifiée qui démarre l'icône du mode dynamique à l'ouverture de session, sans fenêtre de confirmation. Tu la retrouves aussi dans la page Démarrage.";
    public bool RequiresRestart => false;

    public bool IsApplied() => task.RegisteredPath() is { } p && string.Equals(p, exePath(), StringComparison.OrdinalIgnoreCase);

    public void Apply()
    {
        var path = exePath();
        if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("Emplacement d'Unpark introuvable.");
        ctx.Backup.RecordOriginal(Id, Name, ExistedKey, BackupValue.OfText(task.RegisteredPath() is null ? "non" : "oui"));
        task.Register(path);
        ctx.Log.Info($"[{Name}] tâche planifiée créée : « {path} » --tray");
    }

    public void Revert()
    {
        if (ctx.Backup.Get(Id) is null)
        {
            ctx.Log.Info($"[{Name}] rien à annuler (aucune sauvegarde)");
            return;
        }
        if (ctx.Backup.GetOriginal(Id, ExistedKey)?.Text != "oui")
        {
            task.Delete();
            ctx.Log.Info($"[{Name}] tâche planifiée supprimée");
        }
        ctx.Backup.RemoveEntry(Id);
    }
}
