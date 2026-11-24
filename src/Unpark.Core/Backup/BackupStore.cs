using System.Text.Json;
using System.Text.Json.Serialization;
using Unpark.Core.Logging;
using Unpark.Core.Registry;

namespace Unpark.Core.Backup;

/// <summary>Valeur d'origine sauvegardée : une valeur de registre, un texte (ex. GUID du plan actif), ou « n'existait pas ».</summary>
public sealed record BackupValue
{
    public bool Exists { get; init; }
    public RegValue? Registry { get; init; }
    public string? Text { get; init; }

    public static BackupValue Absent { get; } = new() { Exists = false };
    public static BackupValue Of(RegValue? value) => value is null ? Absent : new() { Exists = true, Registry = value };
    public static BackupValue OfText(string? value) => value is null ? Absent : new() { Exists = true, Text = value };

    public override string ToString() => !Exists ? "(absente)" : Registry?.ToString() ?? $"\"{Text}\"";
}

/// <summary>Les valeurs d'origine d'un réglage, enregistrées avant sa première modification.</summary>
public sealed class BackupEntry
{
    public required string SettingId { get; init; }
    public required string SettingName { get; init; }
    public DateTime FirstAppliedUtc { get; init; }
    public Dictionary<string, BackupValue> Originals { get; init; } = [];
}

public sealed class BackupFile
{
    public int FormatVersion { get; set; } = 1;
    /// <summary>Dans l'ordre d'application : « Tout annuler » les défait dans l'ordre inverse.</summary>
    public List<BackupEntry> Entries { get; set; } = [];
    /// <summary>Réglages qui attendent un redémarrage, avec l'heure de la modification (UTC).</summary>
    public Dictionary<string, DateTime> PendingRestarts { get; set; } = [];
    public DateTime? RestorePointOfferedUtc { get; set; }
}

/// <summary>
/// Sauvegarde des valeurs d'origine : %AppData%\Unpark\backup.json.
/// Règles : une valeur d'origine n'est JAMAIS écrasée (appliquer deux fois garde l'état d'avant Unpark) ;
/// le fichier est écrit d'un coup (fichier temporaire puis remplacement) ; un fichier abîmé est mis de côté, jamais écrasé.
/// </summary>
public sealed class BackupStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _file;
    private readonly IActionLog _log;
    private readonly Func<DateTime> _utcNow;
    private readonly object _lock = new();
    private BackupFile _data;

    public BackupStore(string file, IActionLog log, Func<DateTime>? utcNow = null)
    {
        _file = file;
        _log = log;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _data = Load();
    }

    /// <summary>Message à montrer si le fichier de sauvegarde était illisible au démarrage.</summary>
    public string? LoadWarning { get; private set; }

    public IReadOnlyList<BackupEntry> Entries
    {
        get { lock (_lock) return _data.Entries.ToList(); }
    }

    public bool HasAnyEntry
    {
        get { lock (_lock) return _data.Entries.Count > 0; }
    }

    public BackupEntry? Get(string settingId)
    {
        lock (_lock) return _data.Entries.FirstOrDefault(e => e.SettingId == settingId);
    }

    /// <summary>Enregistre la valeur d'origine, seulement si elle n'est pas déjà sauvegardée.</summary>
    public void RecordOriginal(string settingId, string settingName, string key, BackupValue value)
    {
        lock (_lock)
        {
            var entry = _data.Entries.FirstOrDefault(e => e.SettingId == settingId);
            if (entry is null)
            {
                entry = new BackupEntry { SettingId = settingId, SettingName = settingName, FirstAppliedUtc = _utcNow() };
                _data.Entries.Add(entry);
            }
            if (entry.Originals.ContainsKey(key)) return;
            entry.Originals[key] = value;
            Save();
        }
    }

    public BackupValue? GetOriginal(string settingId, string key)
    {
        lock (_lock)
        {
            var entry = _data.Entries.FirstOrDefault(e => e.SettingId == settingId);
            return entry is not null && entry.Originals.TryGetValue(key, out var v) ? v : null;
        }
    }

    /// <summary>À appeler une fois le réglage annulé avec succès.</summary>
    public void RemoveEntry(string settingId)
    {
        lock (_lock)
        {
            if (_data.Entries.RemoveAll(e => e.SettingId == settingId) > 0) Save();
        }
    }

    public void MarkRestartNeeded(string settingId)
    {
        lock (_lock)
        {
            _data.PendingRestarts[settingId] = _utcNow();
            Save();
        }
    }

    /// <summary>Réglages modifiés depuis le dernier démarrage de Windows (les plus anciens sont oubliés).</summary>
    public IReadOnlyList<string> PendingRestarts(DateTime lastBootUtc)
    {
        lock (_lock)
        {
            var done = _data.PendingRestarts.Where(p => p.Value <= lastBootUtc).Select(p => p.Key).ToList();
            if (done.Count > 0)
            {
                foreach (var id in done) _data.PendingRestarts.Remove(id);
                Save();
            }
            return _data.PendingRestarts.Keys.ToList();
        }
    }

    public DateTime? RestorePointOfferedUtc
    {
        get { lock (_lock) return _data.RestorePointOfferedUtc; }
    }

    public void MarkRestorePointOffered()
    {
        lock (_lock)
        {
            _data.RestorePointOfferedUtc = _utcNow();
            Save();
        }
    }

    private BackupFile Load()
    {
        if (!File.Exists(_file)) return new BackupFile();
        try
        {
            return JsonSerializer.Deserialize<BackupFile>(File.ReadAllText(_file), Json) ?? throw new JsonException("fichier vide");
        }
        catch (Exception e)
        {
            // On ne repart JAMAIS de zéro en écrasant un fichier abîmé : on le met de côté pour pouvoir le récupérer.
            var aside = Path.Combine(Path.GetDirectoryName(_file)!, $"backup.illisible-{_utcNow():yyyyMMdd-HHmmss}.json");
            try { File.Move(_file, aside); } catch { /* laissé en place */ }
            LoadWarning = $"Le fichier de sauvegarde était illisible. Il a été mis de côté ({Path.GetFileName(aside)}) : les réglages faits avant ne peuvent plus être annulés automatiquement.";
            _log.Error($"Sauvegarde illisible, mise de côté dans {aside}", e);
            return new BackupFile();
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        var tmp = _file + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_data, Json));
        File.Move(tmp, _file, overwrite: true);
    }
}
