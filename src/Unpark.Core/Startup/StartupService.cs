using Unpark.Core.Backup;
using Unpark.Core.Registry;
using Unpark.Core.Settings;

namespace Unpark.Core.Startup;

/// <summary>
/// Liste des programmes au démarrage, et activation / désactivation comme le Gestionnaire des tâches :
/// la valeur StartupApproved correspondante passe à 03 + date (désactivé) ou 02 (activé). Aucune entrée n'est supprimée.
/// Les clés StartupApproved ne sont pas documentées par Microsoft, mais c'est le mécanisme du Gestionnaire des tâches
/// depuis Windows 8 ; le changement est sauvegardé et réversible.
/// </summary>
public sealed class StartupService(SettingContext ctx, ILogonTaskApi tasks, StartupFolders folders)
{
    private const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    private static readonly (StartupSource Source, RegHive Hive, string RunKey, RegHive ApprovedHive, string ApprovedKey)[] RunKeys =
    [
        (StartupSource.RunUser, RegHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", RegHive.CurrentUser, Approved + @"\Run"),
        (StartupSource.RunMachine, RegHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", RegHive.LocalMachine, Approved + @"\Run"),
        (StartupSource.RunMachine32, RegHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", RegHive.LocalMachine, Approved + @"\Run32"),
    ];

    public IReadOnlyList<StartupEntry> List()
    {
        var list = new List<StartupEntry>();
        foreach (var (source, hive, runKey, approvedHive, approvedKey) in RunKeys)
        {
            foreach (var name in ctx.Registry.ValueNames(hive, runKey))
            {
                var command = ctx.Registry.Get(new RegPath(hive, runKey, name))?.Text ?? "";
                var approved = new RegPath(approvedHive, approvedKey, name);
                list.Add(new StartupEntry(source, name, command, IsEnabled(ctx.Registry.Get(approved)), approved, null));
            }
        }
        AddFolder(list, StartupSource.FolderUser, folders.User, RegHive.CurrentUser);
        AddFolder(list, StartupSource.FolderCommon, folders.Common, RegHive.LocalMachine);
        try
        {
            list.AddRange(tasks.List().Select(t => new StartupEntry(StartupSource.LogonTask, t.Name, t.Command, t.Enabled, null, t.Path)));
        }
        catch (Exception e)
        {
            ctx.Log.Warn($"Tâches planifiées illisibles : {e.Message}");
        }
        return list.OrderBy(e => e.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private void AddFolder(List<StartupEntry> list, StartupSource source, string dir, RegHive approvedHive)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            var name = Path.GetFileName(file);
            if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            var approved = new RegPath(approvedHive, Approved + @"\StartupFolder", name);
            list.Add(new StartupEntry(source, name, file, IsEnabled(ctx.Registry.Get(approved)), approved, null));
        }
    }

    /// <summary>Absente = activé. Sinon, premier octet pair (02, 06) = activé, impair (03) = désactivé.</summary>
    public static bool IsEnabled(RegValue? approved) =>
        approved is not { Kind: RegKind.Binary, Bytes: { Length: > 0 } bytes } || (bytes[0] & 1) == 0;

    /// <summary>
    /// Réglage qui met l'entrée dans l'état voulu. Si Unpark l'avait déjà changée et qu'on revient à l'état d'origine,
    /// renvoie revert = true : il faut ANNULER (remise exacte de la valeur d'origine) plutôt qu'appliquer.
    /// </summary>
    public (ISetting Setting, bool Revert) Toggle(StartupEntry entry, bool enable)
    {
        var setting = new StartupToggle(ctx, tasks, entry, enable);
        return (setting, OriginalEnabled(entry) == enable);
    }

    /// <summary>État d'avant Unpark (null si Unpark n'a pas touché à cette entrée).</summary>
    public bool? OriginalEnabled(StartupEntry entry)
    {
        if (ctx.Backup.Get(entry.Id) is null) return null;
        if (entry.ApprovedPath is { } path)
            return ctx.Backup.GetOriginal(entry.Id, path.BackupKey) is { } v && (!v.Exists || IsEnabled(v.Registry));
        return ctx.Backup.GetOriginal(entry.Id, StartupToggle.TaskKey(entry))?.Text == "1";
    }

    /// <summary>Pour « Tout annuler » : retrouve le réglage d'une entrée à partir de son identifiant.</summary>
    public ISetting? Resolve(string id)
    {
        if (!id.StartsWith("startup:", StringComparison.Ordinal)) return null;
        var entry = List().FirstOrDefault(e => e.Id == id);
        return entry is null ? null : new StartupToggle(ctx, tasks, entry, entry.Enabled);
    }
}

/// <summary>Active ou désactive une entrée de démarrage, avec sauvegarde de l'état d'origine.</summary>
public sealed class StartupToggle(SettingContext ctx, ILogonTaskApi tasks, StartupEntry entry, bool enable) : ISetting
{
    internal static string TaskKey(StartupEntry e) => $"text:task:{e.TaskPath}";

    public string Id => entry.Id;
    public string Name => $"Démarrage : {entry.DisplayName}";
    public string Description => enable ? "Lancer au démarrage de Windows" : "Ne plus lancer au démarrage de Windows";
    public bool RequiresRestart => false;

    public bool IsApplied() => CurrentEnabled() == enable;

    private bool CurrentEnabled() => entry.ApprovedPath is { } path
        ? StartupService.IsEnabled(ctx.Registry.Get(path))
        : tasks.List().FirstOrDefault(t => t.Path == entry.TaskPath)?.Enabled ?? entry.Enabled;

    public void Apply()
    {
        if (entry.ApprovedPath is { } path)
        {
            var current = ctx.Registry.Get(path);
            ctx.Backup.RecordOriginal(Id, Name, path.BackupKey, BackupValue.Of(current));
            var value = enable ? EnabledValue() : DisabledValue(DateTime.UtcNow);
            ctx.Registry.Set(path, value);
            ctx.Log.Info($"[{Name}] {path} : {current?.ToString() ?? "(absente)"} → {value}");
        }
        else
        {
            ctx.Backup.RecordOriginal(Id, Name, TaskKey(entry), BackupValue.OfText(CurrentEnabled() ? "1" : "0"));
            tasks.SetEnabled(entry.TaskPath!, enable);
            ctx.Log.Info($"[{Name}] tâche {entry.TaskPath} : {(enable ? "activée" : "désactivée")}");
        }
    }

    public void Revert()
    {
        if (entry.ApprovedPath is null && ctx.Backup.GetOriginal(Id, TaskKey(entry))?.Text is { } original)
        {
            tasks.SetEnabled(entry.TaskPath!, original == "1");
            ctx.Log.Info($"[{Name}] tâche {entry.TaskPath} remise à l'origine : {(original == "1" ? "activée" : "désactivée")}");
        }
        RegistrySetting.RevertFromBackup(ctx, Id, Name); // valeur StartupApproved d'origine + suppression de la sauvegarde
    }

    /// <summary>Comme le Gestionnaire des tâches : 02 suivi de zéros.</summary>
    public static RegValue EnabledValue() => RegValue.Binary([0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);

    /// <summary>Comme le Gestionnaire des tâches : 03 00 00 00 + date de désactivation (FILETIME).</summary>
    public static RegValue DisabledValue(DateTime utc)
    {
        var bytes = new byte[12];
        bytes[0] = 0x03;
        BitConverter.GetBytes(utc.ToFileTimeUtc()).CopyTo(bytes, 4);
        return RegValue.Binary(bytes);
    }
}
