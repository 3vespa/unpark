using System.Text;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using Unpark.Core.Updates;

namespace Unpark.Tests;

/// <summary>Clé de test (jamais la vraie clé, qui reste sur le serveur) et fabrication de fichiers .sig.</summary>
public sealed class TestSigner
{
    private readonly Ed25519PrivateKeyParameters _private;
    public byte[] PublicKey { get; }

    public TestSigner()
    {
        var gen = new Ed25519KeyPairGenerator();
        gen.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var pair = gen.GenerateKeyPair();
        _private = (Ed25519PrivateKeyParameters)pair.Private;
        PublicKey = ((Ed25519PublicKeyParameters)pair.Public).GetEncoded();
    }

    public string Sign(UpdateManifest m)
    {
        var bytes = Encoding.UTF8.GetBytes(m.SignedText);
        var signer = new Ed25519Signer();
        signer.Init(true, _private);
        signer.BlockUpdate(bytes, 0, bytes.Length);
        return m.SignedText + "signature: " + Convert.ToBase64String(signer.GenerateSignature()) + "\n";
    }

    public static UpdateManifest ManifestFor(string version, byte[] exe) =>
        new(version, UpdateManifest.ExpectedFileName(version), exe.Length,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(exe)).ToLowerInvariant());
}

public sealed class FakeUpdateHttp : IUpdateHttp
{
    public Dictionary<string, byte[]> Files { get; } = [];
    public List<Uri> Requested { get; } = [];
    public Exception? Throw { get; set; }
    public TimeSpan? LastTimeout { get; private set; }

    public Task<string> GetStringAsync(Uri url, TimeSpan timeout, long maxBytes, CancellationToken cancel)
    {
        Requested.Add(url);
        LastTimeout = timeout;
        if (Throw is not null) throw Throw;
        return Files.TryGetValue(url.ToString(), out var b) ? Task.FromResult(Encoding.UTF8.GetString(b)) : throw new HttpRequestException("404");
    }

    public async Task DownloadAsync(Uri url, Stream destination, long maxBytes, IProgress<long>? progress, CancellationToken cancel)
    {
        Requested.Add(url);
        if (!Files.TryGetValue(url.ToString(), out var b)) throw new HttpRequestException("404");
        await destination.WriteAsync(b, cancel);
        progress?.Report(b.Length);
    }
}

public class UpdateManifestTests
{
    private static readonly byte[] Exe = Encoding.UTF8.GetBytes("MZ faux exe de test");

    [Fact]
    public void Signature_valide_acceptee()
    {
        var signer = new TestSigner();
        var m = TestSigner.ManifestFor("0.10.0", Exe);
        var parsed = UpdateManifest.ParseAndVerify(signer.Sign(m), signer.PublicKey);
        Assert.Equal(m, parsed);
        // Fichier écrit sous Windows (fins de ligne CRLF) : accepté aussi.
        Assert.Equal(m, UpdateManifest.ParseAndVerify(signer.Sign(m).Replace("\n", "\r\n"), signer.PublicKey));
    }

    [Fact]
    public void Signature_d_une_autre_cle_refusee()
    {
        var m = TestSigner.ManifestFor("0.10.0", Exe);
        var e = Assert.Throws<UpdateRejectedException>(() => UpdateManifest.ParseAndVerify(new TestSigner().Sign(m), new TestSigner().PublicKey));
        Assert.Contains("Signature invalide", e.Message);
    }

    [Theory]
    [InlineData("version: 0.10.0", "version: 0.11.0")] // version changée après signature
    [InlineData("size: ", "size: 1")]                   // taille changée
    public void Contenu_modifie_apres_signature_refuse(string from, string to)
    {
        var signer = new TestSigner();
        var m = TestSigner.ManifestFor("0.10.0", Exe);
        var text = signer.Sign(m).Replace(from, to);
        if (to == "version: 0.11.0") text = text.Replace("file: Unpark-v0.10.0.exe", "file: Unpark-v0.11.0.exe");
        Assert.Throws<UpdateRejectedException>(() => UpdateManifest.ParseAndVerify(text, signer.PublicKey));
    }

    [Fact]
    public void Empreinte_modifiee_refusee()
    {
        var signer = new TestSigner();
        var m = TestSigner.ManifestFor("0.10.0", Exe);
        var text = signer.Sign(m).Replace(m.Sha256, new string('0', 64));
        Assert.Throws<UpdateRejectedException>(() => UpdateManifest.ParseAndVerify(text, signer.PublicKey));
    }

    [Theory]
    [InlineData("")]
    [InlineData("n'importe quoi")]
    [InlineData("Unpark update 1\nversion: 0.10.0\n")]
    public void Fichier_illisible_refuse(string text) =>
        Assert.Throws<UpdateRejectedException>(() => UpdateManifest.ParseAndVerify(text, new TestSigner().PublicKey));

    [Fact]
    public void Nom_de_fichier_qui_ne_correspond_pas_a_la_version_refuse()
    {
        var signer = new TestSigner();
        var m = TestSigner.ManifestFor("0.10.0", Exe) with { FileName = "Unpark-v0.9.0.exe" };
        Assert.Throws<UpdateRejectedException>(() => UpdateManifest.ParseAndVerify(signer.Sign(m), signer.PublicKey));
    }

    /// <summary>
    /// Signature produite par OpenSSL, exactement comme scripts/prepare-release.sh (openssl pkeyutl -sign -rawin),
    /// avec une clé de test jetable : prouve que l'appli accepte ce que produit la routine de release.
    /// </summary>
    [Fact]
    public void Signature_produite_par_openssl_acceptee()
    {
        var publicKey = Convert.FromBase64String(OpenSslFixture.PublicKey);
        var m = UpdateManifest.ParseAndVerify(OpenSslFixture.Sig, publicKey);
        Assert.Equal("0.10.0", m.Version);
        Assert.Equal("Unpark-v0.10.0.exe", m.FileName);
    }

    [Fact]
    public void Cle_publique_integree_valide()
    {
        Assert.Equal(32, UpdateKeys.PublicKey.Length);
        Assert.Equal("3vespa/unpark", UpdateKeys.Repository);
        Assert.Equal("https://api.github.com/repos/3vespa/unpark/releases/latest", UpdateKeys.LatestReleaseApi.ToString());
    }

    [Fact]
    public void Verification_du_fichier_taille_et_empreinte()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "a.exe");
        File.WriteAllBytes(path, Exe);
        var m = TestSigner.ManifestFor("0.10.0", Exe);
        m.VerifyFile(path);

        File.WriteAllBytes(path, Exe.Concat(new byte[] { 1 }).ToArray());
        Assert.Contains("Taille", Assert.Throws<UpdateRejectedException>(() => m.VerifyFile(path)).Message);

        var altered = (byte[])Exe.Clone();
        altered[0] ^= 1;
        File.WriteAllBytes(path, altered);
        Assert.Contains("SHA-256", Assert.Throws<UpdateRejectedException>(() => m.VerifyFile(path)).Message);
    }

    [Theory]
    [InlineData("0.10.0", "0.9.1", true)]
    [InlineData("0.9.10", "0.9.9", true)]
    [InlineData("1.0", "0.99.99", true)]
    [InlineData("0.10.0.1", "0.10.0", true)]
    [InlineData("0.10.0", "0.10.0", false)]
    [InlineData("0.10", "0.10.0", false)]
    [InlineData("0.9.1", "0.10.0", false)]
    [InlineData("v0.10.0", "0.9.1", true)]
    [InlineData("0.10.0-beta", "0.9.1", false)]
    [InlineData("abc", "0.9.1", false)]
    public void Comparaison_des_versions(string candidate, string current, bool newer) =>
        Assert.Equal(newer, UpdateVersion.IsNewer(candidate, current));
}

/// <summary>Généré avec : openssl genpkey -algorithm ed25519 ; openssl pkeyutl -sign -rawin (clé de test jetable).</summary>
internal static class OpenSslFixture
{
    public const string PublicKey = "7XF5JzQZ12/bRN8cyGVAFnG5Cg+AlGFXf77GJ26mFBQ=";
    public const string Sig = "Unpark update 1\nversion: 0.10.0\nfile: Unpark-v0.10.0.exe\nsize: 11\nsha256: 397c5a52890458249a2444a61a0b542bdec49d78008c3db7410172f5263fc181\nsignature: HZIu0r09YKXF5pMmJIYkJdLu/bsVu1Jj1NQAF0BsjGoseZy//agQEogqv0JJRK+vsg2RziDYm3kkAxOW8KoZAQ==\n";
}

public class UpdateServiceTests
{
    private const string Api = "https://api.github.com/repos/3vespa/unpark/releases/latest";
    private static readonly byte[] Exe = Encoding.UTF8.GetBytes("MZ nouvelle version 0.10.0");

    private static string ReleaseJson(string tag, params string[] assets) =>
        $$"""{ "tag_name": "{{tag}}", "draft": false, "prerelease": false, "assets": [{{string.Join(",", assets.Select(a => $$"""{ "name": "{{a}}", "size": 26, "browser_download_url": "https://evil.example/{{a}}" }"""))}}] }""";

    private static (UpdateService Service, FakeUpdateHttp Http, TestSigner Signer, TempDir Dir) Setup(string current = "0.9.1")
    {
        var dir = new TempDir();
        var http = new FakeUpdateHttp();
        var signer = new TestSigner();
        var service = new UpdateService(http, new MemoryLog(), Path.Combine(dir.Path, "updates"), current, signer.PublicKey);
        return (service, http, signer, dir);
    }

    private static void Publish(FakeUpdateHttp http, TestSigner signer, string version, byte[] exe, string? sigOverride = null)
    {
        var tag = "v" + version;
        http.Files[Api] = Encoding.UTF8.GetBytes(ReleaseJson(tag, $"Unpark-v{version}.exe", $"Unpark-v{version}.exe.sig"));
        http.Files[$"https://github.com/3vespa/unpark/releases/download/{tag}/Unpark-v{version}.exe"] = exe;
        http.Files[$"https://github.com/3vespa/unpark/releases/download/{tag}/Unpark-v{version}.exe.sig"] =
            Encoding.UTF8.GetBytes(sigOverride ?? signer.Sign(TestSigner.ManifestFor(version, exe)));
    }

    [Fact]
    public async Task Verification_en_5_secondes_et_adresses_de_3vespa_unpark_seulement()
    {
        var (service, http, signer, dir) = Setup();
        using var _ = dir;
        Publish(http, signer, "0.10.0", Exe);
        var info = await service.CheckAsync();
        Assert.NotNull(info);
        Assert.Equal("0.10.0", info.Version);
        Assert.Equal(TimeSpan.FromSeconds(5), http.LastTimeout);
        // Les adresses sont construites par Unpark, jamais reprises de la réponse (ici « evil.example »).
        Assert.Equal("https://github.com/3vespa/unpark/releases/download/v0.10.0/Unpark-v0.10.0.exe", info.ExeUrl.ToString());
        Assert.Equal("https://github.com/3vespa/unpark/releases/download/v0.10.0/Unpark-v0.10.0.exe.sig", info.SignatureUrl.ToString());
        Assert.All(http.Requested, u => Assert.Equal("https", u.Scheme));
    }

    [Fact]
    public async Task Sans_internet_echec_silencieux()
    {
        var (service, http, _, dir) = Setup();
        using var _d = dir;
        http.Throw = new HttpRequestException("Aucun réseau");
        Assert.Null(await service.CheckAsync());
        http.Throw = new TaskCanceledException("délai dépassé");
        Assert.Null(await service.CheckAsync());
    }

    [Theory]
    [InlineData("v0.9.1")]  // même version
    [InlineData("v0.9.0")]  // plus ancienne
    [InlineData("nightly")] // tag inattendu
    public void Pas_de_mise_a_jour_si_pas_plus_recente(string tag) =>
        Assert.Null(UpdateService.ParseLatestRelease(ReleaseJson(tag, $"Unpark-{tag}.exe", $"Unpark-{tag}.exe.sig"), "0.9.1"));

    [Fact]
    public void Release_sans_signature_ignoree() =>
        Assert.Null(UpdateService.ParseLatestRelease(ReleaseJson("v0.10.0", "Unpark-v0.10.0.exe"), "0.9.1"));

    [Fact]
    public void Preversion_ignoree() =>
        Assert.Null(UpdateService.ParseLatestRelease(ReleaseJson("v0.10.0", "Unpark-v0.10.0.exe", "Unpark-v0.10.0.exe.sig").Replace("\"prerelease\": false", "\"prerelease\": true"), "0.9.1"));

    [Fact]
    public async Task Telechargement_verifie_puis_pret()
    {
        var (service, http, signer, dir) = Setup();
        using var _ = dir;
        Publish(http, signer, "0.10.0", Exe);
        var progress = new List<double>();
        var staged = await service.DownloadAsync((await service.CheckAsync())!, new SyncProgress<double>(progress.Add));
        Assert.Equal("0.10.0", staged.Version);
        Assert.Equal(Exe, File.ReadAllBytes(staged.ExePath));
        Assert.Equal(1.0, progress.Last());
        Assert.Empty(Directory.GetFiles(service.UpdatesDir, "*.part"));
        // Au lancement suivant, elle est retrouvée et revérifiée.
        Assert.Equal("0.10.0", service.FindStaged()?.Version);
    }

    [Fact]
    public async Task Exe_modifie_refuse_et_rien_n_est_garde()
    {
        var (service, http, signer, dir) = Setup();
        using var _ = dir;
        Publish(http, signer, "0.10.0", Exe);
        var info = (await service.CheckAsync())!;
        var tampered = (byte[])Exe.Clone();
        tampered[^1] ^= 1;
        http.Files[info.ExeUrl.ToString()] = tampered;
        var e = await Assert.ThrowsAsync<UpdateRejectedException>(() => service.DownloadAsync(info, null));
        Assert.Contains("SHA-256", e.Message);
        Assert.Empty(Directory.GetFiles(service.UpdatesDir));
        Assert.Null(service.FindStaged());
    }

    [Fact]
    public async Task Signature_invalide_refusee_avant_de_telecharger_l_exe()
    {
        var (service, http, _, dir) = Setup();
        using var _d = dir;
        Publish(http, new TestSigner(), "0.10.0", Exe); // signé avec une autre clé
        var info = (await service.CheckAsync())!;
        var e = await Assert.ThrowsAsync<UpdateRejectedException>(() => service.DownloadAsync(info, null));
        Assert.Contains("Signature invalide", e.Message);
        Assert.DoesNotContain(http.Requested, u => u == info.ExeUrl);
    }

    [Fact]
    public async Task Vieil_exe_signe_presente_comme_nouvelle_version_refuse()
    {
        var (service, http, signer, dir) = Setup();
        using var _ = dir;
        // Release v0.10.0 qui contient en fait la signature (valide) de la v0.9.0.
        var old = Encoding.UTF8.GetBytes("MZ ancienne 0.9.0");
        Publish(http, signer, "0.10.0", old, sigOverride: signer.Sign(TestSigner.ManifestFor("0.9.0", old)));
        var info = (await service.CheckAsync())!;
        await Assert.ThrowsAsync<UpdateRejectedException>(() => service.DownloadAsync(info, null));
    }

    [Fact]
    public async Task Fichier_en_attente_abime_supprime_au_lancement()
    {
        var (service, http, signer, dir) = Setup();
        using var _ = dir;
        Publish(http, signer, "0.10.0", Exe);
        var staged = await service.DownloadAsync((await service.CheckAsync())!, null);
        File.WriteAllText(staged.ExePath, "remplacé par un autre programme");
        Assert.Null(service.FindStaged());
        Assert.Empty(Directory.GetFiles(service.UpdatesDir));
    }

    [Fact]
    public async Task Fichier_en_attente_deja_installe_supprime()
    {
        var (service, http, signer, dir) = Setup();
        using var _ = dir;
        Publish(http, signer, "0.10.0", Exe);
        await service.DownloadAsync((await service.CheckAsync())!, null);
        var after = new UpdateService(http, new MemoryLog(), service.UpdatesDir, "0.10.0", signer.PublicKey);
        Assert.Null(after.FindStaged());
        Assert.Empty(Directory.GetFiles(service.UpdatesDir));
    }

    [Fact]
    public void Preparation_depuis_un_dossier_local_memes_verifications()
    {
        var (service, _, signer, dir) = Setup();
        using var _d = dir;
        var folder = Path.Combine(dir.Path, "cle-usb");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "Unpark-v0.10.0.exe"), Exe);
        File.WriteAllText(Path.Combine(folder, "Unpark-v0.10.0.exe.sig"), signer.Sign(TestSigner.ManifestFor("0.10.0", Exe)));
        Assert.Equal("0.10.0", service.StageFromFolder(folder).Version);

        File.WriteAllText(Path.Combine(folder, "Unpark-v0.10.0.exe.sig"), new TestSigner().Sign(TestSigner.ManifestFor("0.10.0", Exe)));
        Assert.Throws<UpdateRejectedException>(() => service.StageFromFolder(folder));
    }

    [Fact]
    public async Task Reglages_et_sauvegarde_jamais_touches()
    {
        var (service, http, signer, dir) = Setup();
        using var _ = dir;
        var backup = Path.Combine(dir.Path, "backup.json");
        var prefs = Path.Combine(dir.Path, "preferences.json");
        File.WriteAllText(backup, "{ \"sauvegarde\": true }");
        File.WriteAllText(prefs, "{ \"AutoUpdate\": true }");
        Publish(http, signer, "0.10.0", Exe);
        await service.DownloadAsync((await service.CheckAsync())!, null);
        service.DeleteStaged();
        Assert.Equal("{ \"sauvegarde\": true }", File.ReadAllText(backup));
        Assert.Equal("{ \"AutoUpdate\": true }", File.ReadAllText(prefs));
    }
}

/// <summary>IProgress sans contexte de synchronisation (Progress&lt;T&gt; appelle en différé).</summary>
public sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}

public class UpdateInstallerTests
{
    private static readonly byte[] NewExe = Encoding.UTF8.GetBytes("MZ nouvelle");
    private static readonly byte[] OldExe = Encoding.UTF8.GetBytes("MZ ancienne");

    private static (StagedUpdate Update, string Current, TempDir Dir) Setup()
    {
        var dir = new TempDir();
        var staged = Path.Combine(dir.Path, "updates", "Unpark-v0.10.0.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
        File.WriteAllBytes(staged, NewExe);
        var install = Path.Combine(dir.Path, "Bureau");
        Directory.CreateDirectory(install);
        var current = Path.Combine(install, "Unpark-v0.9.1.exe");
        File.WriteAllBytes(current, OldExe);
        return (new StagedUpdate(TestSigner.ManifestFor("0.10.0", NewExe), staged, staged + ".sig"), current, dir);
    }

    [Fact]
    public void Remplacement_au_meme_endroit_et_sous_le_meme_nom()
    {
        var (update, current, dir) = Setup();
        using var _ = dir;
        new UpdateInstaller(new MemoryLog()).Install(update, current);
        Assert.Equal(NewExe, File.ReadAllBytes(current));
        Assert.Equal(OldExe, File.ReadAllBytes(current + ".old"));
        Assert.False(File.Exists(current + ".new"));

        // Lancement suivant : le .old est supprimé.
        Assert.True(new UpdateInstaller(new MemoryLog()).CleanupLeftovers(current, TimeSpan.Zero));
        Assert.False(File.Exists(current + ".old"));
        Assert.Equal(NewExe, File.ReadAllBytes(current));
    }

    [Theory]
    [InlineData("copy")]       // copie impossible (disque plein…)
    [InlineData("sha")]        // copie abîmée
    [InlineData("move-old")]   // l'exe en cours ne peut pas être renommé
    [InlineData("move-new")]   // le nouveau ne peut pas prendre sa place
    public void Echec_a_n_importe_quelle_etape_ancienne_version_intacte(string failAt)
    {
        var (update, current, dir) = Setup();
        using var _ = dir;
        var ops = new FailingFileOps(failAt, current);
        Assert.ThrowsAny<Exception>(() => new UpdateInstaller(new MemoryLog(), ops).Install(update, current));
        Assert.Equal(OldExe, File.ReadAllBytes(current));
        Assert.False(File.Exists(current + ".new"));
        Assert.False(File.Exists(current + ".old"));
    }

    [Fact]
    public void Old_verrouille_rien_n_est_modifie()
    {
        var (update, current, dir) = Setup();
        using var _ = dir;
        File.WriteAllText(current + ".old", "ancienne instance encore ouverte");
        var ops = new FailingFileOps("delete-old", current);
        Assert.ThrowsAny<Exception>(() => new UpdateInstaller(new MemoryLog(), ops).Install(update, current));
        Assert.Equal(OldExe, File.ReadAllBytes(current));
    }

    [Fact]
    public void Old_verrouille_au_lancement_reessai_plus_tard()
    {
        var (_, current, dir) = Setup();
        using var _d = dir;
        File.WriteAllText(current + ".old", "x");
        var log = new MemoryLog();
        Assert.False(new UpdateInstaller(log, new FailingFileOps("delete-old", current)).CleanupLeftovers(current, TimeSpan.Zero));
        Assert.Contains(log.Lines, l => l.StartsWith("WARN") && l.Contains(".old"));
        Assert.True(File.Exists(current));
    }

    private sealed class FailingFileOps(string failAt, string current) : IUpdateFileOps
    {
        private readonly RealUpdateFileOps _real = new();
        public void Copy(string source, string destination)
        {
            if (failAt == "copy") throw new IOException("Disque plein (simulé)");
            _real.Copy(source, destination);
        }
        public void Move(string source, string destination)
        {
            if (failAt == "move-old" && source == current) throw new UnauthorizedAccessException("Accès refusé (simulé)");
            if (failAt == "move-new" && destination == current && source.EndsWith(".new")) throw new IOException("Fichier occupé (simulé)");
            _real.Move(source, destination);
        }
        public void Delete(string path)
        {
            if (failAt == "delete-old" && path.EndsWith(".old")) throw new IOException("Fichier verrouillé (simulé)");
            _real.Delete(path);
        }
        public bool Exists(string path) => _real.Exists(path);
        public string Sha256(string path) => failAt == "sha" ? new string('0', 64) : _real.Sha256(path);
    }
}

public class PreferencesTests
{
    [Fact]
    public void Mises_a_jour_automatiques_activees_par_defaut_et_memorisees()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "preferences.json");
        var prefs = new Unpark.Core.PreferencesStore(file, new MemoryLog());
        Assert.True(prefs.AutoUpdate);
        prefs.AutoUpdate = false;
        Assert.False(new Unpark.Core.PreferencesStore(file, new MemoryLog()).AutoUpdate);
    }

    [Fact]
    public void Fichier_abime_valeurs_par_defaut()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "preferences.json");
        File.WriteAllText(file, "{ pas du json");
        Assert.True(new Unpark.Core.PreferencesStore(file, new MemoryLog()).AutoUpdate);
    }
}
