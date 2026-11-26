using System.Text.Json;
using Unpark.Core.Logging;

namespace Unpark.Core;

/// <summary>Préférences de l'appli (%AppData%\Unpark\preferences.json). Séparées de backup.json, qui ne contient que des valeurs d'origine.</summary>
public sealed class Preferences
{
    /// <summary>Rechercher, télécharger et installer les mises à jour automatiquement (activé par défaut).</summary>
    public bool AutoUpdate { get; set; } = true;
}

public sealed class PreferencesStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _file;
    private readonly IActionLog _log;
    private readonly object _lock = new();
    private Preferences _value;

    public PreferencesStore(string file, IActionLog log)
    {
        _file = file;
        _log = log;
        _value = Load();
    }

    public bool AutoUpdate
    {
        get { lock (_lock) return _value.AutoUpdate; }
        set
        {
            lock (_lock)
            {
                if (_value.AutoUpdate == value) return;
                _value.AutoUpdate = value;
                Save();
            }
            _log.Info($"Préférence : mises à jour automatiques {(value ? "activées" : "désactivées")}");
        }
    }

    private Preferences Load()
    {
        try
        {
            if (File.Exists(_file)) return JsonSerializer.Deserialize<Preferences>(File.ReadAllText(_file)) ?? new Preferences();
        }
        catch (Exception e)
        {
            _log.Warn($"Préférences illisibles, valeurs par défaut utilisées ({e.Message})");
        }
        return new Preferences();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            var tmp = _file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_value, Json));
            File.Move(tmp, _file, overwrite: true);
        }
        catch (Exception e)
        {
            _log.Error("Préférences : enregistrement impossible", e);
        }
    }
}
