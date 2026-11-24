using Unpark.Core;
using Unpark.Core.Cleanup;

namespace Unpark.Tests;

public sealed class FakeCleanupSystem : ICleanupSystem
{
    public long BinBytes = 5000, BinItems = 3;
    public HashSet<string> Running { get; } = ["wuauserv", "bits"];
    public List<string> Events { get; } = [];
    public bool FailStop;

    public (long Bytes, long Items) QueryRecycleBin() => (BinBytes, BinItems);
    public void EmptyRecycleBin() { BinBytes = 0; BinItems = 0; }
    public bool IsServiceRunning(string name) => Running.Contains(name);
    public void StopService(string name)
    {
        if (FailStop) throw new InvalidOperationException("refus (simulé)");
        Running.Remove(name);
        Events.Add("stop " + name);
    }
    public void StartService(string name) { Running.Add(name); Events.Add("start " + name); }
}

public class CleanupTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly FakeCleanupSystem _sys = new();
    private readonly MemoryLog _log = new();
    private readonly CleanupPaths _paths;
    private static readonly DateTime Now = DateTime.UtcNow;

    public CleanupTests()
    {
        string D(string n) => Directory.CreateDirectory(Path.Combine(_dir.Path, n)).FullName;
        _paths = new CleanupPaths(D("temp"), D("wintemp"), D("wu"));
    }

    public void Dispose()
    {
        foreach (var d in Directory.EnumerateDirectories(_dir.Path, "*", SearchOption.AllDirectories))
            try { File.SetUnixFileMode(d, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); } catch { }
        _dir.Dispose();
    }

    private CleanupService Service() => new(_sys, _log, _paths, () => Now);

    private string Write(string relative, int size, TimeSpan age)
    {
        var path = Path.Combine(_dir.Path, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
        File.SetLastWriteTimeUtc(path, Now - age);
        return path;
    }

    [Fact]
    public void Analyse_puis_nettoyage_avec_espace_gagne()
    {
        var old = Write("temp/old.tmp", 1000, TimeSpan.FromDays(3));
        Write("temp/sub/deep.log", 500, TimeSpan.FromDays(1));
        var recent = Write("temp/installer-en-cours.msi", 700, TimeSpan.FromMinutes(10));

        var scan = Service().Scan().Single(s => s.Target.Id == "temp-user");
        Assert.Equal(1500, scan.Bytes);           // le fichier récent n'est pas compté
        Assert.Equal(2, scan.Files);

        var result = Service().Clean(["temp-user"]).Single();
        Assert.Equal(1500, result.FreedBytes);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));         // moins de 2 h : épargné
        Assert.False(Directory.Exists(Path.Combine(_paths.UserTemp, "sub")));  // dossier vide supprimé
        Assert.True(Directory.Exists(_paths.UserTemp));                          // la racine reste
    }

    [Fact]
    public void Fichiers_proteges_ignores_sans_planter()
    {
        Write("temp/ok.tmp", 100, TimeSpan.FromDays(1));
        Write("temp/locked/a.tmp", 100, TimeSpan.FromDays(1));
        File.SetUnixFileMode(Path.Combine(_paths.UserTemp, "locked"), UnixFileMode.UserRead | UnixFileMode.UserExecute);

        var result = Service().Clean(["temp-user"]).Single();
        Assert.Equal(1, result.Deleted);
        Assert.Equal(1, result.Skipped);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Les_liens_ne_sont_jamais_suivis()
    {
        var precious = Write("documents/precieux.txt", 100, TimeSpan.FromDays(30));
        Directory.CreateSymbolicLink(Path.Combine(_paths.UserTemp, "lien"), Path.GetDirectoryName(precious)!);
        Service().Clean(["temp-user"]);
        Assert.True(File.Exists(precious));
    }

    [Fact]
    public void Windows_Update_arrete_puis_toujours_relance()
    {
        Write("wu/update.cab", 4000, TimeSpan.Zero);
        var result = Service().Clean(["wu-cache"]).Single();
        Assert.Equal(4000, result.FreedBytes);
        Assert.Equal(["stop wuauserv", "stop bits", "start bits", "start wuauserv"], _sys.Events);
        Assert.Contains("wuauserv", _sys.Running);
    }

    [Fact]
    public void Service_qui_ne_tournait_pas_reste_arrete()
    {
        _sys.Running.Remove("bits");
        Service().Clean(["wu-cache"]);
        Assert.DoesNotContain("bits", _sys.Running);
    }

    [Fact]
    public void Si_Windows_Update_refuse_la_pause_rien_n_est_supprime()
    {
        var cab = Write("wu/update.cab", 4000, TimeSpan.Zero);
        _sys.FailStop = true;
        var result = Service().Clean(["wu-cache"]).Single();
        Assert.NotNull(result.Error);
        Assert.True(File.Exists(cab));
    }

    [Fact]
    public void Corbeille_et_plus_de_cache_des_miniatures()
    {
        var results = Service().Clean(["recycle", "thumbs"]);
        Assert.Equal(5000, results.Single(r => r.Target.Id == "recycle").FreedBytes);
        Assert.DoesNotContain(Service().Targets, t => t.Id == "thumbs");   // retiré après l'audit
    }

    [Theory]
    [InlineData(0, "0 octet")]
    [InlineData(900, "900 octets")]
    [InlineData(12_288, "12 Ko")]
    [InlineData(1_288_490_189, "1,2 Go")]
    public void Tailles_en_francais(long bytes, string expected) => Assert.Equal(expected, Format.Bytes(bytes));
}
