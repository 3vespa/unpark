namespace Unpark.Core.Updates;

/// <summary>Origine et clé des mises à jour : fixées dans l'exe, jamais lues d'un fichier ou d'un réglage.</summary>
public static class UpdateKeys
{
    /// <summary>Seul dépôt dont les releases sont acceptées.</summary>
    public const string Repository = "3vespa/unpark";

    /// <summary>
    /// Clé publique Ed25519 (32 octets, base64) qui vérifie les mises à jour.
    /// La clé privée correspondante reste sur le serveur de compilation (~/.config/unpark/update-signing-key.pem), hors du dépôt.
    /// Créée par scripts/make-signing-key.sh. La changer empêche les versions déjà installées de se mettre à jour seules.
    /// </summary>
    public const string PublicKeyBase64 = "T4p7YlaAeXQ2DZ274KvlQQTeF6Hxj/PbwGgaZB/79vo=";

    public static byte[] PublicKey => Convert.FromBase64String(PublicKeyBase64);

    /// <summary>Dernière release publique (les brouillons et préversions n'y apparaissent pas).</summary>
    public static Uri LatestReleaseApi => new($"https://api.github.com/repos/{Repository}/releases/latest");

    /// <summary>Adresse d'un fichier d'une release : construite ici, jamais reprise telle quelle de la réponse de GitHub.</summary>
    public static Uri AssetUrl(string tag, string fileName) =>
        new($"https://github.com/{Repository}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(fileName)}");
}
