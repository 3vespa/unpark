namespace Unpark.Core;

/// <summary>Emplacements des fichiers d'Unpark : %AppData%\Unpark (sauvegarde) et %AppData%\Unpark\logs (journal).</summary>
public sealed class AppPaths(string root)
{
    public static AppPaths Default { get; } =
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Unpark"));

    public string Root { get; } = root;
    public string BackupFile => Path.Combine(Root, "backup.json");
    public string LogsDir => Path.Combine(Root, "logs");
    public string DebloatHistoryFile => Path.Combine(Root, "debloat.json");
}
