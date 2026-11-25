using Unpark.Core.Backup;
using Unpark.Core.Disks;
using Unpark.Core.Settings;

namespace Unpark.Tests;

public sealed class FakeDiskSystem : IDiskSystem
{
    public List<VolumeInfo> List { get; } = [];
    public Dictionary<char, DateTime> Last { get; } = [];
    public Dictionary<char, int> Fragmentation { get; } = [];
    public List<string> DefragCalls { get; } = [];
    /// <summary>Sortie simulée de defrag.exe (avec les « \r » de l'avancement).</summary>
    public string[] Output { get; set; } = [];
    public int ExitCode { get; set; }
    /// <summary>Annule pendant l'exécution, après cette ligne.</summary>
    public int? CancelAfterLine { get; set; }
    public CancellationTokenSource? Cts { get; set; }

    public IReadOnlyList<VolumeInfo> Volumes() => List;
    public IReadOnlyDictionary<char, DateTime> LastOptimizations() => Last;
    public int FragmentationPercent(char letter) => Fragmentation[letter];

    public int RunDefrag(string arguments, Action<string> onOutput, CancellationToken cancel)
    {
        DefragCalls.Add(arguments);
        for (var i = 0; i < Output.Length; i++)
        {
            onOutput(Output[i]);
            if (CancelAfterLine == i) Cts!.Cancel();
            cancel.ThrowIfCancellationRequested();
        }
        return ExitCode;
    }
}

public class DiskTests
{
    private static readonly DateTime Now = new(2026, 11, 25, 15, 0, 0);
    private readonly FakeDiskSystem _sys = new();
    private readonly MemoryLog _log = new();
    private DiskOptimizer Optimizer => new(_sys, _log, () => Now);

    private static VolumeInfo Ssd(char l = 'C') => new(l, "Windows", MediaKind.Ssd, "SSD NVMe", 1_000_000_000_000);
    private static VolumeInfo Hdd(char l = 'D') => new(l, "Jeux", MediaKind.Hdd, "Disque dur", 2_000_000_000_000);
    private static VolumeInfo Unknown(char l = 'E') => new(l, "", MediaKind.Unknown, "Type inconnu", 500_000_000);

    [Fact]
    public void SSD_TRIM_et_disque_dur_defragmentation()
    {
        Assert.Equal("C: /L /U /V /H", DiskOptimizer.DefragArguments('C', MediaKind.Ssd));
        Assert.Equal("D: /D /U /V /H", DiskOptimizer.DefragArguments('d', MediaKind.Hdd));
        Assert.Equal(OptimizeOperation.Retrim, DiskOptimizer.OperationFor(MediaKind.Ssd));
        Assert.Equal(OptimizeOperation.Defrag, DiskOptimizer.OperationFor(MediaKind.Hdd));
    }

    [Fact]
    public void Jamais_de_defragmentation_sur_un_SSD()
    {
        foreach (var letter in "CDEFGZ")
        {
            var args = DiskOptimizer.DefragArguments(letter, MediaKind.Ssd);
            Assert.Contains("/L", args);
            Assert.DoesNotContain("/D", args);
            Assert.DoesNotContain("/O", args);   // /O laisserait Windows choisir
            Assert.DoesNotContain("/X", args);   // consolidation de l'espace libre = défragmentation
        }
        _sys.Output = ["Retrim: 100% complete."];
        Optimizer.Optimize(Ssd(), null, CancellationToken.None);
        Assert.All(_sys.DefragCalls, c => Assert.DoesNotContain("/D", c));
    }

    [Fact]
    public void Type_inconnu_jamais_optimise_a_l_aveugle()
    {
        Assert.Null(DiskOptimizer.OperationFor(MediaKind.Unknown));
        Assert.Throws<InvalidOperationException>(() => DiskOptimizer.DefragArguments('E', MediaKind.Unknown));
        var result = Optimizer.Optimize(Unknown(), null, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Empty(_sys.DefragCalls);
    }

    [Fact]
    public void Lettre_invalide_refusee()
    {
        Assert.Throws<ArgumentException>(() => DiskOptimizer.DefragArguments('1', MediaKind.Ssd));
        Assert.Throws<ArgumentException>(() => DiskOptimizer.DefragArguments('&', MediaKind.Hdd));
    }

    [Theory]
    [InlineData("Retrim:  12% complete...", 12)]
    [InlineData("Réajustement : 45 % effectué...", 45)]
    [InlineData("Défragmentation : 100 % effectué.", 100)]
    [InlineData("Analysis: 3.5% complete...", 3)]
    [InlineData("Pass 2: 7% complete... Defragmentation: 81% complete...", 81)]
    public void Avancement_lu_en_anglais_et_en_francais(string line, int expected)
    {
        Assert.Equal(expected, DiskOptimizer.ParseProgress(line)?.Percent);
    }

    [Theory]
    [InlineData("Invoking retrim on Windows (C:)...")]
    [InlineData("")]
    [InlineData("Valeur : 250%")]
    public void Ligne_sans_avancement_ignoree(string line) => Assert.Null(DiskOptimizer.ParseProgress(line));

    [Fact]
    public void Optimisation_transmet_l_avancement_et_reussit()
    {
        _sys.Output = ["Invoking retrim on Windows (C:)...", "Retrim:  10% complete...", "Retrim:  55% complete...", "Retrim: 100% complete.", "The operation completed successfully."];
        var seen = new List<int>();
        var result = Optimizer.Optimize(Ssd(), new SyncProgress<OptimizeProgress>(p => seen.Add(p.Percent)), CancellationToken.None);
        Assert.True(result.Success, result.Message);
        Assert.Equal([10, 55, 100], seen);
        Assert.Contains("TRIM", result.Message);
    }

    [Fact]
    public void Code_d_erreur_de_defrag_signale()
    {
        _sys.Output = ["The parameter is incorrect. (0x89000004)"];
        _sys.ExitCode = unchecked((int)0x89000004);
        var result = Optimizer.Optimize(Hdd(), null, CancellationToken.None);
        Assert.False(result.Success);
        Assert.False(result.Cancelled);
        Assert.Contains("0x89000004", result.Message);
    }

    [Fact]
    public void Arreter_en_cours_de_route()
    {
        using var cts = new CancellationTokenSource();
        _sys.Cts = cts;
        _sys.CancelAfterLine = 1;
        _sys.Output = ["Defragmentation: 5% complete...", "Defragmentation: 9% complete...", "Defragmentation: 50% complete..."];
        var result = Optimizer.Optimize(Hdd(), null, cts.Token);
        Assert.True(result.Cancelled);
        Assert.False(result.Success);
    }

    [Fact]
    public void Analyse_seulement_pour_les_disques_durs()
    {
        _sys.Fragmentation['D'] = 12;
        Assert.Equal(12, Optimizer.Analyze(Hdd()));
        Assert.Throws<InvalidOperationException>(() => Optimizer.Analyze(Ssd()));
    }

    [Fact]
    public void Etats_comme_l_outil_Windows()
    {
        Assert.Equal("OK", DiskOptimizer.StateText(MediaKind.Ssd, Now.AddDays(-3), null, Now));
        Assert.Equal("Optimisation recommandée", DiskOptimizer.StateText(MediaKind.Ssd, Now.AddDays(-40), null, Now));
        Assert.Equal("Optimisation recommandée", DiskOptimizer.StateText(MediaKind.Ssd, null, null, Now));
        Assert.Equal("12 % fragmenté — optimisation recommandée", DiskOptimizer.StateText(MediaKind.Hdd, Now, 12, Now));
        Assert.Equal("OK (3 % fragmenté)", DiskOptimizer.StateText(MediaKind.Hdd, null, 3, Now));
        Assert.Equal("Analyse conseillée", DiskOptimizer.StateText(MediaKind.Hdd, null, null, Now));
        Assert.Equal("OK", DiskOptimizer.StateText(MediaKind.Hdd, Now.AddDays(-2), null, Now));
        Assert.StartsWith("Type inconnu", DiskOptimizer.StateText(MediaKind.Unknown, Now, null, Now));
        Assert.True(DiskOptimizer.NeedsAttention(MediaKind.Hdd, null, 15, Now));
        Assert.False(DiskOptimizer.NeedsAttention(MediaKind.Ssd, Now, null, Now));
        Assert.False(DiskOptimizer.NeedsAttention(MediaKind.Unknown, null, null, Now));
    }

    [Fact]
    public void Date_de_derniere_optimisation()
    {
        Assert.Equal("Inconnue", DiskOptimizer.LastText(null, Now));
        Assert.Equal("Aujourd'hui à 09:30", DiskOptimizer.LastText(Now.Date.AddHours(9.5), Now));
        Assert.Equal("Hier", DiskOptimizer.LastText(Now.AddDays(-1), Now));
        Assert.Equal("18/11/2026 (il y a 7 jours)", DiskOptimizer.LastText(Now.AddDays(-7), Now));
    }

    [Theory]
    [InlineData(4, 11, MediaKind.Ssd, "SSD")]
    [InlineData(4, 17, MediaKind.Ssd, "SSD NVMe")]
    [InlineData(0, 17, MediaKind.Ssd, "SSD NVMe")]   // NVMe sans type précisé
    [InlineData(3, 11, MediaKind.Hdd, "Disque dur")]
    [InlineData(0, 7, MediaKind.Unknown, "Type inconnu")]   // clé USB
    [InlineData(0, 11, MediaKind.Unknown, "Type inconnu")]
    public void Type_de_disque_depuis_WMI(int media, int bus, MediaKind kind, string text)
    {
        Assert.Equal((kind, text), DiskOptimizer.Classify(media, bus));
    }

    [Fact]
    public void Journal_illisible_ne_bloque_pas_la_liste()
    {
        var optimizer = new DiskOptimizer(new ThrowingLast(), _log, () => Now);
        Assert.Empty(optimizer.LastOptimizations());
        Assert.Contains(_log.Lines, l => l.StartsWith("WARN"));
    }

    private sealed class ThrowingLast : IDiskSystem
    {
        public IReadOnlyList<VolumeInfo> Volumes() => [];
        public IReadOnlyDictionary<char, DateTime> LastOptimizations() => throw new UnauthorizedAccessException("journal refusé");
        public int FragmentationPercent(char letter) => 0;
        public int RunDefrag(string arguments, Action<string> onOutput, CancellationToken cancel) => 0;
    }

    /// <summary>IProgress synchrone (Progress&lt;T&gt; passe par le pool de threads).</summary>
    private sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}

public sealed class FakeScheduled : IScheduledOptimizationApi
{
    public ScheduledState? State { get; set; } = new(false, "P7D");
    public ScheduledState? Read() => State;
    public void SetEnabled(bool enabled) => State = State! with { Enabled = enabled };
}

public class ScheduledOptimizationTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly MemoryLog _log = new();
    private readonly FakeScheduled _api = new();
    private readonly BackupStore _backup;
    private readonly ScheduledOptimizationSetting _setting;

    public ScheduledOptimizationTests()
    {
        _backup = new BackupStore(Path.Combine(_dir.Path, "backup.json"), _log);
        _setting = new ScheduledOptimizationSetting(new SettingContext(new FakeRegistry(), _backup, _log), _api);
    }

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Activer_puis_annuler_remet_desactivee()
    {
        Assert.Equal("Désactivée", _setting.StatusText());
        Assert.False(_setting.IsApplied());
        _setting.Apply();
        Assert.True(_api.State!.Enabled);
        Assert.Equal("Activée — chaque semaine", _setting.StatusText());
        _setting.Revert();
        Assert.False(_api.State!.Enabled);
        Assert.False(_backup.HasAnyEntry);
    }

    [Fact]
    public void Deja_activee_l_annulation_la_laisse_activee()
    {
        _api.State = new(true, "P1M");
        _setting.Apply();
        _setting.Revert();
        Assert.True(_api.State!.Enabled);
    }

    [Fact]
    public void Tout_annuler_retrouve_le_reglage()
    {
        var ctx = new SettingContext(new FakeRegistry(), _backup, _log);
        var engine = new SettingsEngine(ctx, [_setting], () => DateTime.UtcNow);
        Assert.True(engine.Apply(_setting).Success);
        var results = engine.RevertAll();
        Assert.All(results, r => Assert.True(r.Success));
        Assert.False(_api.State!.Enabled);
    }

    [Fact]
    public void Tache_introuvable()
    {
        _api.State = null;
        Assert.NotNull(_setting.UnavailableReason());
        Assert.Equal("Introuvable sur ce PC", _setting.StatusText());
        Assert.Throws<InvalidOperationException>(_setting.Apply);
        Assert.False(_backup.HasAnyEntry);
    }

    [Theory]
    [InlineData("P1D", "chaque jour")]
    [InlineData("P7D", "chaque semaine")]
    [InlineData("P1W", "chaque semaine")]
    [InlineData("P1M", "chaque mois")]
    [InlineData("P30D", "chaque mois")]
    [InlineData("P14D", "toutes les 2 semaines")]
    [InlineData("P3D", "tous les 3 jours")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("P", null)]
    [InlineData("n'importe quoi", null)]
    public void Frequence_lisible(string? period, string? expected) =>
        Assert.Equal(expected, ScheduledOptimizationSetting.FrequencyText(period));
}
