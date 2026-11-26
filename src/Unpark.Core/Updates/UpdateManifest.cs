using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Unpark.Core.Updates;

/// <summary>Mise à jour refusée (signature, empreinte, version ou origine incorrecte). Le message s'affiche tel quel.</summary>
public sealed class UpdateRejectedException(string message) : Exception(message);

/// <summary>
/// Fichier « Unpark-vX.Y.Z.exe.sig » publié avec chaque release, écrit et signé par scripts/prepare-release.sh :
/// <code>
/// Unpark update 1
/// version: 0.10.0
/// file: Unpark-v0.10.0.exe
/// size: 73068681
/// sha256: 8ef99961…
/// signature: (Ed25519 des 5 lignes ci-dessus, chacune terminée par \n, en base64)
/// </code>
/// La signature couvre l'empreinte SHA-256 de l'exe (donc l'exe lui-même), sa taille, son nom ET sa version :
/// impossible de faire passer un vieil exe signé pour une version plus récente.
/// </summary>
public sealed partial record UpdateManifest(string Version, string FileName, long Size, string Sha256)
{
    public const string Header = "Unpark update 1";

    /// <summary>Texte exact qui est signé.</summary>
    public string SignedText => $"{Header}\nversion: {Version}\nfile: {FileName}\nsize: {Size.ToString(CultureInfo.InvariantCulture)}\nsha256: {Sha256}\n";

    /// <summary>Lit le fichier .sig et vérifie la signature avec la clé publique. Lève UpdateRejectedException sinon.</summary>
    public static UpdateManifest ParseAndVerify(string text, byte[] publicKey)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var lines = text.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0 || lines[0].Trim() != Header)
            throw new UpdateRejectedException("Fichier de signature illisible (format inconnu).");
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) throw new UpdateRejectedException("Fichier de signature illisible.");
            if (!fields.TryAdd(line[..colon].Trim(), line[(colon + 1)..].Trim()))
                throw new UpdateRejectedException("Fichier de signature illisible (ligne en double).");
        }

        string Field(string name) => fields.TryGetValue(name, out var v) && v.Length > 0
            ? v : throw new UpdateRejectedException($"Fichier de signature incomplet (« {name} » manquant).");

        var version = Field("version");
        var file = Field("file");
        var sha = Field("sha256").ToLowerInvariant();
        if (!long.TryParse(Field("size"), NumberStyles.None, CultureInfo.InvariantCulture, out var size) || size <= 0)
            throw new UpdateRejectedException("Fichier de signature illisible (taille).");
        if (!Sha256Regex().IsMatch(sha))
            throw new UpdateRejectedException("Fichier de signature illisible (empreinte SHA-256).");
        if (UpdateVersion.Parse(version) is null)
            throw new UpdateRejectedException("Fichier de signature illisible (version).");
        if (file != ExpectedFileName(version))
            throw new UpdateRejectedException("Le nom de fichier signé ne correspond pas à la version.");

        byte[] signature;
        try { signature = Convert.FromBase64String(Field("signature")); }
        catch (FormatException) { throw new UpdateRejectedException("Signature illisible."); }

        var manifest = new UpdateManifest(version, file, size, sha);
        if (!VerifySignature(Encoding.UTF8.GetBytes(manifest.SignedText), signature, publicKey))
            throw new UpdateRejectedException("Signature invalide : cette mise à jour n'a pas été signée par Unpark. Elle a été refusée.");
        return manifest;
    }

    /// <summary>Signature Ed25519 (RFC 8032) d'un message.</summary>
    public static bool VerifySignature(byte[] message, byte[] signature, byte[] publicKey)
    {
        if (signature.Length != 64 || publicKey.Length != 32) return false;
        try
        {
            var verifier = new Ed25519Signer();
            verifier.Init(false, new Ed25519PublicKeyParameters(publicKey, 0));
            verifier.BlockUpdate(message, 0, message.Length);
            return verifier.VerifySignature(signature);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Vérifie la taille et l'empreinte SHA-256 d'un exe téléchargé. Lève UpdateRejectedException sinon.</summary>
    public void VerifyFile(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new UpdateRejectedException("Fichier de mise à jour introuvable.");
        if (info.Length != Size)
            throw new UpdateRejectedException($"Taille du fichier incorrecte ({info.Length} octets au lieu de {Size}) : mise à jour refusée.");
        if (ComputeSha256(path) != Sha256)
            throw new UpdateRejectedException("Empreinte SHA-256 incorrecte : le fichier téléchargé n'est pas celui qui a été signé. Mise à jour refusée.");
    }

    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static string ExpectedFileName(string version) => $"Unpark-v{version}.exe";

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Regex();
}

/// <summary>Numéros de version (« 0.10.0 », « 1.2.3.4 ») : comparaison sans dépendre de la culture.</summary>
public static partial class UpdateVersion
{
    public static Version? Parse(string? text)
    {
        if (text is null) return null;
        var t = text.Trim();
        if (t.StartsWith('v') || t.StartsWith('V')) t = t[1..];
        return VersionRegex().IsMatch(t) && Version.TryParse(t, out var v) ? v : null;
    }

    /// <summary>true si <paramref name="candidate"/> est strictement plus récente que <paramref name="current"/>.</summary>
    public static bool IsNewer(string candidate, string current) =>
        Parse(candidate) is { } c && Parse(current) is { } cur && Normalize(c) > Normalize(cur);

    // 0.10 == 0.10.0 == 0.10.0.0
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    [GeneratedRegex(@"^\d{1,5}(\.\d{1,5}){1,3}$")]
    private static partial Regex VersionRegex();
}
