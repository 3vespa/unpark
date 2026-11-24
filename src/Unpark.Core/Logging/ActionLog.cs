namespace Unpark.Core.Logging;

public interface IActionLog
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);
}

/// <summary>
/// Journal de toutes les actions : un fichier par jour, %AppData%\Unpark\logs\unpark-AAAA-MM-JJ.log.
/// Les journaux de plus de 60 jours sont supprimés au démarrage. Une erreur d'écriture du journal ne bloque jamais l'appli.
/// </summary>
public sealed class ActionLog : IActionLog
{
    private const int KeepDays = 60;
    private readonly string _dir;
    private readonly Func<DateTime> _now;
    private readonly object _lock = new();

    public ActionLog(string dir, Func<DateTime>? now = null)
    {
        _dir = dir;
        _now = now ?? (() => DateTime.Now);
        try
        {
            Directory.CreateDirectory(_dir);
            PurgeOld();
        }
        catch
        {
            // Dossier inaccessible : on continue sans journal plutôt que de planter.
        }
    }

    public string CurrentFile => Path.Combine(_dir, $"unpark-{_now():yyyy-MM-dd}.log");

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("ATTENTION", message);
    public void Error(string message, Exception? exception = null) =>
        Write("ERREUR", exception is null ? message : $"{message} — {exception.GetType().Name} : {exception.Message}");

    private void Write(string level, string message)
    {
        var line = $"{_now():yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}";
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(_dir);
                File.AppendAllText(CurrentFile, line);
            }
            catch
            {
                // Pas de journal plutôt qu'un plantage.
            }
        }
    }

    private void PurgeOld()
    {
        var limit = _now().Date.AddDays(-KeepDays);
        foreach (var file in Directory.EnumerateFiles(_dir, "unpark-*.log"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (DateTime.TryParseExact(name["unpark-".Length..], "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var day) && day < limit)
                File.Delete(file);
        }
    }
}
