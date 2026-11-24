using Unpark.Core.Logging;
using Unpark.Core.Registry;
using Unpark.Core.RestorePoints;

namespace Unpark.Tests;

/// <summary>Faux registre en mémoire.</summary>
public sealed class FakeRegistry : IRegistry
{
    public Dictionary<RegPath, RegValue> Values { get; } = [];
    /// <summary>Si défini, Set() lève une erreur pour ce chemin (simule un accès refusé).</summary>
    public RegPath? FailOn { get; set; }

    public RegValue? Get(RegPath path) => Values.TryGetValue(path, out var v) ? v : null;

    public void Set(RegPath path, RegValue value)
    {
        if (path == FailOn) throw new UnauthorizedAccessException("Accès refusé (simulé)");
        Values[path] = value;
    }

    public void Delete(RegPath path) => Values.Remove(path);
}

public sealed class MemoryLog : IActionLog
{
    public List<string> Lines { get; } = [];
    public void Info(string message) => Lines.Add("INFO " + message);
    public void Warn(string message) => Lines.Add("WARN " + message);
    public void Error(string message, Exception? exception = null) => Lines.Add("ERROR " + message);
}

public sealed class FakeRestoreApi : IRestorePointApi
{
    public DateTime? Latest { get; set; }
    /// <summary>Ce que fait Windows quand on demande une création : par défaut, en crée un « maintenant ».</summary>
    public Func<DateTime?>? OnCreate { get; set; }
    public int Calls { get; private set; }
    public int FallbackCalls { get; private set; }
    public Action? DuringCreate { get; set; }
    /// <summary>Simule une méthode WMI en échec.</summary>
    public Exception? WmiThrows { get; set; }
    public uint FallbackCode { get; set; }

    public DateTime? GetLatestUtc() => Latest;

    public uint Create(string description)
    {
        Calls++;
        DuringCreate?.Invoke();
        if (WmiThrows is not null) throw WmiThrows;
        Latest = OnCreate is null ? DateTime.UtcNow : OnCreate();
        return 0;
    }

    public uint CreateFallback(string description)
    {
        FallbackCalls++;
        if (FallbackCode != 0) return FallbackCode;
        Latest = OnCreate is null ? DateTime.UtcNow : OnCreate();
        return 0;
    }
}

/// <summary>Dossier temporaire supprimé à la fin du test.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "unpark-tests-" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { }
    }
}
