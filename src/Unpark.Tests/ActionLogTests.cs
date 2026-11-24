using Unpark.Core.Logging;

namespace Unpark.Tests;

public class ActionLogTests
{
    [Fact]
    public void Ecrit_un_fichier_par_jour_et_supprime_les_vieux()
    {
        using var dir = new TempDir();
        var old = Path.Combine(dir.Path, "unpark-2026-01-01.log");
        File.WriteAllText(old, "vieux");
        var log = new ActionLog(dir.Path, () => new DateTime(2026, 11, 24, 16, 40, 12));
        log.Info("Appliquer : test");
        log.Error("Échec", new InvalidOperationException("boum"));

        Assert.False(File.Exists(old));
        var lines = File.ReadAllLines(Path.Combine(dir.Path, "unpark-2026-11-24.log"));
        Assert.Equal("2026-11-24 16:40:12 [INFO] Appliquer : test", lines[0]);
        Assert.Contains("[ERREUR] Échec — InvalidOperationException : boum", lines[1]);
    }
}
