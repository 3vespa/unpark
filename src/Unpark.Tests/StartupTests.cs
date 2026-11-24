using Unpark.Core.Backup;
using Unpark.Core.Registry;
using Unpark.Core.Settings;
using Unpark.Core.Startup;

namespace Unpark.Tests;

public sealed class FakeTasks : ILogonTaskApi
{
    public List<LogonTask> Tasks { get; } = [];
    public IReadOnlyList<LogonTask> List() => Tasks.ToList();
    public void SetEnabled(string path, bool enabled)
    {
        var i = Tasks.FindIndex(t => t.Path == path);
        Tasks[i] = Tasks[i] with { Enabled = enabled };
    }
}

public class StartupTests : IDisposable
{
    private const string Run = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRun = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private readonly TempDir _dir = new();
    private readonly FakeRegistry _reg = new();
    private readonly MemoryLog _log = new();
    private readonly FakeTasks _tasks = new();
    private readonly BackupStore _backup;
    private readonly StartupService _service;
    private readonly SettingContext _ctx;

    public StartupTests()
    {
        _backup = new BackupStore(Path.Combine(_dir.Path, "backup.json"), _log);
        _ctx = new SettingContext(_reg, _backup, _log);
        var user = Directory.CreateDirectory(Path.Combine(_dir.Path, "user")).FullName;
        var common = Directory.CreateDirectory(Path.Combine(_dir.Path, "common")).FullName;
        File.WriteAllText(Path.Combine(user, "Discord.lnk"), "");
        File.WriteAllText(Path.Combine(user, "desktop.ini"), "");
        _service = new StartupService(_ctx, _tasks, new StartupFolders(user, common));

        _reg.Values[new RegPath(RegHive.CurrentUser, Run, "Steam")] = RegValue.String("\"C:\\Steam\\steam.exe\" -silent");
        _reg.Values[new RegPath(RegHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "RtkAudio")] = RegValue.String("rtk.exe");
        _tasks.Tasks.Add(new LogonTask(@"\OneDrive Updater", "OneDrive Updater", "onedrive.exe /update", true));
    }

    public void Dispose() => _dir.Dispose();
    private StartupEntry Entry(string name) => _service.List().Single(e => e.Name == name);

    private async Task<OperationResult> Toggle(string name, bool enable)
    {
        var engine = new SettingsEngine(_ctx, [], () => DateTime.MinValue, _service.Resolve);
        var (setting, revert) = _service.Toggle(Entry(name), enable);
        return await Task.FromResult(revert ? engine.Revert(setting) : engine.Apply(setting));
    }

    [Fact]
    public void Liste_registre_dossier_et_taches()
    {
        var names = _service.List().Select(e => e.DisplayName).ToList();
        Assert.Equal(["Discord", "OneDrive Updater", "RtkAudio", "Steam"], names);
        Assert.All(_service.List(), e => Assert.True(e.Enabled));
    }

    [Fact]
    public async Task Desactiver_comme_le_Gestionnaire_des_taches_puis_reactiver_a_l_identique()
    {
        await Toggle("Steam", enable: false);
        var approved = _reg.Get(new RegPath(RegHive.CurrentUser, ApprovedRun, "Steam"))!;
        Assert.Equal(0x03, approved.Bytes![0]);
        Assert.Equal(12, approved.Bytes.Length);
        Assert.False(Entry("Steam").Enabled);
        Assert.NotNull(_reg.Get(new RegPath(RegHive.CurrentUser, Run, "Steam"))); // entrée jamais supprimée

        await Toggle("Steam", enable: true);
        Assert.Null(_reg.Get(new RegPath(RegHive.CurrentUser, ApprovedRun, "Steam"))); // absente, comme avant
        Assert.False(_backup.HasAnyEntry);
    }

    [Fact]
    public async Task Entree_deja_desactivee_par_l_utilisateur_retrouve_sa_valeur_exacte()
    {
        var path = new RegPath(RegHive.CurrentUser, ApprovedRun, "Steam");
        var original = StartupToggle.DisabledValue(new DateTime(2025, 3, 1, 10, 0, 0, DateTimeKind.Utc));
        _reg.Values[path] = original;
        Assert.False(Entry("Steam").Enabled);

        await Toggle("Steam", enable: true);
        Assert.Equal(0x02, _reg.Get(path)!.Bytes![0]);
        await Toggle("Steam", enable: false);
        Assert.True(original.SameAs(_reg.Get(path)));   // même date qu'avant, pas une nouvelle
    }

    [Fact]
    public async Task Dossier_Demarrage_et_tache_planifiee()
    {
        await Toggle("Discord.lnk", enable: false);
        Assert.False(Entry("Discord.lnk").Enabled);
        Assert.True(File.Exists(Path.Combine(_dir.Path, "user", "Discord.lnk")));   // fichier jamais supprimé

        await Toggle("OneDrive Updater", enable: false);
        Assert.False(_tasks.Tasks[0].Enabled);
        await Toggle("OneDrive Updater", enable: true);
        Assert.True(_tasks.Tasks[0].Enabled);
    }

    [Fact]
    public async Task Tout_annuler_retrouve_les_entrees_de_demarrage()
    {
        await Toggle("Steam", enable: false);
        await Toggle("OneDrive Updater", enable: false);
        var engine = new SettingsEngine(_ctx, [], () => DateTime.MinValue, _service.Resolve);
        Assert.All(engine.RevertAll(), r => Assert.True(r.Success));
        Assert.True(Entry("Steam").Enabled);
        Assert.True(_tasks.Tasks[0].Enabled);
        Assert.False(_backup.HasAnyEntry);
    }

    [Fact]
    public async Task Programme_desinstalle_depuis_Tout_annuler_nettoie_quand_meme()
    {
        await Toggle("Steam", enable: false);
        _reg.Delete(new RegPath(RegHive.CurrentUser, Run, "Steam"));   // Steam désinstallé
        var engine = new SettingsEngine(_ctx, [], () => DateTime.MinValue, _service.Resolve);
        Assert.All(engine.RevertAll(), r => Assert.True(r.Success));
        Assert.Null(_reg.Get(new RegPath(RegHive.CurrentUser, ApprovedRun, "Steam")));
    }
}
