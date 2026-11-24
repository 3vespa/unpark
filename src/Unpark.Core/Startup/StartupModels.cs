using Unpark.Core.Registry;

namespace Unpark.Core.Startup;

public enum StartupSource
{
    RunUser,
    RunMachine,
    RunMachine32,
    FolderUser,
    FolderCommon,
    LogonTask,
}

/// <summary>Un programme lancé au démarrage de Windows.</summary>
public sealed record StartupEntry(
    StartupSource Source,
    string Name,
    string Command,
    bool Enabled,
    /// <summary>Valeur StartupApproved qui active/désactive l'entrée (null pour une tâche planifiée).</summary>
    RegPath? ApprovedPath,
    /// <summary>Chemin de la tâche planifiée (null sinon).</summary>
    string? TaskPath)
{
    /// <summary>Identifiant stable, utilisé comme clé dans backup.json.</summary>
    public string Id => $"startup:{Source}:{(Source == StartupSource.LogonTask ? TaskPath : Name)}";

    public string DisplayName => Source is StartupSource.FolderUser or StartupSource.FolderCommon && Name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
        ? Name[..^4]
        : Name;

    public string SourceText => Source switch
    {
        StartupSource.RunUser => "Registre (utilisateur)",
        StartupSource.RunMachine => "Registre (tous les utilisateurs)",
        StartupSource.RunMachine32 => "Registre (tous les utilisateurs, 32 bits)",
        StartupSource.FolderUser => "Dossier Démarrage (utilisateur)",
        StartupSource.FolderCommon => "Dossier Démarrage (tous les utilisateurs)",
        StartupSource.LogonTask => "Tâche planifiée (ouverture de session)",
        _ => Source.ToString(),
    };
}

/// <summary>Tâche planifiée déclenchée à l'ouverture de session (hors tâches de Microsoft).</summary>
public sealed record LogonTask(string Path, string Name, string Command, bool Enabled);

/// <summary>Planificateur de tâches. Remplacé par une fausse version dans les tests.</summary>
public interface ILogonTaskApi
{
    IReadOnlyList<LogonTask> List();
    void SetEnabled(string path, bool enabled);
}

/// <summary>Dossiers Démarrage (utilisateur et commun).</summary>
public sealed record StartupFolders(string User, string Common)
{
    public static StartupFolders Default => new(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup));
}
