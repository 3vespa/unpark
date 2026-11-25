using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Unpark.Core.Settings;

namespace Unpark.Core.Legacy;

/// <summary>
/// Ce qu'il reste à défaire du mode dynamique (supprimé en v0.9.1) : la tâche planifiée « \Unpark\Mode dynamique »
/// créée par les versions 0.8 et 0.9.0, et l'icône de ces versions si elle tourne encore.
/// Remplacé par une fausse version dans les tests.
/// </summary>
public interface ILegacyDynamicSystem
{
    /// <summary>Supprime la tâche « \Unpark\Mode dynamique » (et le dossier \Unpark s'il est vide). Sans erreur si elle n'existe pas.</summary>
    void DeleteAutostartTask();
    /// <summary>Demande à l'icône d'une ancienne version de se fermer. true si elle tournait.</summary>
    bool StopRunningTray();
    /// <summary>Efface les réglages du mode dynamique (%AppData%\Unpark\dynamic.json) s'ils existent.</summary>
    void DeleteSettingsFile();
}

/// <summary>
/// « Lancer le mode dynamique avec Windows », réglage des anciennes versions : il ne sait plus que s'annuler.
/// Jamais affiché ; retrouvé seulement par « Tout annuler » à partir de l'identifiant enregistré dans backup.json.
/// Même logique d'annulation qu'en v0.9.0 : la tâche est supprimée seulement si elle n'existait pas avant Unpark.
/// </summary>
public sealed class LegacyDynamicAutostart(SettingContext ctx, ILegacyDynamicSystem system) : ISetting
{
    public const string SettingId = "dynamic.autostart";
    private const string ExistedKey = "text:dynamic.autostartExisted";

    public string Id => SettingId;
    public string Name => ctx.Backup.Get(Id)?.SettingName ?? "Lancer le mode dynamique avec Windows";
    public string Description => "Réglage d'une ancienne version d'Unpark (mode dynamique, supprimé en v0.9.1).";
    public bool RequiresRestart => false;

    public bool IsApplied() => ctx.Backup.Get(Id) is not null;

    public void Apply() => throw new NotSupportedException("Le mode dynamique a été retiré d'Unpark (v0.9.1).");

    public void Revert()
    {
        var name = Name;
        if (ctx.Backup.Get(Id) is null)
        {
            ctx.Log.Info($"[{name}] rien à annuler (aucune sauvegarde)");
            return;
        }
        if (system.StopRunningTray()) ctx.Log.Info($"[{name}] icône du mode dynamique (ancienne version) fermée");
        if (ctx.Backup.GetOriginal(Id, ExistedKey)?.Text != "oui")
        {
            system.DeleteAutostartTask();
            ctx.Log.Info($"[{name}] tâche planifiée « \\Unpark\\Mode dynamique » supprimée");
        }
        else ctx.Log.Info($"[{name}] tâche planifiée conservée (elle existait avant Unpark)");
        try { system.DeleteSettingsFile(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ctx.Log.Warn($"[{name}] dynamic.json non effacé : {e.Message}"); }
        ctx.Backup.RemoveEntry(Id);
    }
}

[SupportedOSPlatform("windows")]
public sealed class WindowsLegacyDynamicSystem(string settingsFile) : ILegacyDynamicSystem
{
    private const string FolderName = "Unpark";
    private const string TaskName = "Mode dynamique";
    /// <summary>Signal d'arrêt écouté par l'icône des versions 0.8 et 0.9.0.</summary>
    private const string QuitEventName = @"Local\Unpark.DynamicMode.Quit";

    public void DeleteAutostartTask()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service") ?? throw new InvalidOperationException("Planificateur de tâches indisponible.");
        dynamic service = Activator.CreateInstance(type)!;
        try
        {
            service.Connect();
            try { service.GetFolder("\\" + FolderName).DeleteTask(TaskName, 0); }
            catch (Exception e) when (e is COMException or FileNotFoundException) { }
            try { service.GetFolder("\\").DeleteFolder(FolderName, 0); } // seulement s'il est vide
            catch (Exception e) when (e is COMException or FileNotFoundException or IOException) { }
        }
        finally { Marshal.FinalReleaseComObject(service); }
    }

    public bool StopRunningTray()
    {
        if (!EventWaitHandle.TryOpenExisting(QuitEventName, out var quit)) return false;
        using (quit) quit.Set();
        return true;
    }

    public void DeleteSettingsFile()
    {
        if (File.Exists(settingsFile)) File.Delete(settingsFile);
    }
}
