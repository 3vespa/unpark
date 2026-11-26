using Unpark.Core.Logging;

namespace Unpark.Core.Updates;

/// <summary>Opérations sur les fichiers de l'installation (remplacées dans les tests pour simuler une panne à chaque étape).</summary>
public interface IUpdateFileOps
{
    void Copy(string source, string destination);
    void Move(string source, string destination);
    void Delete(string path);
    bool Exists(string path);
    string Sha256(string path);
}

public sealed class RealUpdateFileOps : IUpdateFileOps
{
    public void Copy(string source, string destination) => File.Copy(source, destination, overwrite: true);
    public void Move(string source, string destination) => File.Move(source, destination, overwrite: false);
    public void Delete(string path) => File.Delete(path);
    public bool Exists(string path) => File.Exists(path);
    public string Sha256(string path) => UpdateManifest.ComputeSha256(path);
}

/// <summary>
/// Remplacement sûr de l'exe en cours :
/// 1. copie la nouvelle version à côté (« Unpark.exe.new ») et revérifie son empreinte ;
/// 2. renomme l'exe en cours en « Unpark.exe.old » (Windows autorise à renommer un exe qui tourne) ;
/// 3. renomme « .new » en nom d'origine.
/// Si une étape échoue, l'ancien exe est remis à sa place : la version installée reste intacte et fonctionnelle.
/// Le « .old » est supprimé au lancement suivant (CleanupLeftovers). Les réglages (%AppData%\Unpark) ne sont jamais touchés.
/// </summary>
public sealed class UpdateInstaller(IActionLog log, IUpdateFileOps? fileOps = null)
{
    private readonly IUpdateFileOps _fs = fileOps ?? new RealUpdateFileOps();

    public static string OldPath(string exe) => exe + ".old";
    public static string NewPath(string exe) => exe + ".new";

    /// <summary>Installe la mise à jour à la place de <paramref name="currentExe"/>. Lève une exception (exe d'origine intact) si ça échoue.</summary>
    public void Install(StagedUpdate update, string currentExe)
    {
        var newPath = NewPath(currentExe);
        var oldPath = OldPath(currentExe);
        log.Info($"Mise à jour : installation de v{update.Version} à la place de {currentExe}");

        // Restes d'une tentative précédente.
        if (_fs.Exists(newPath)) _fs.Delete(newPath);
        if (_fs.Exists(oldPath)) _fs.Delete(oldPath); // s'il est encore verrouillé, on s'arrête ici : rien n'a été modifié

        // 1. Copie à côté de l'exe (même disque : le renommage final est instantané), puis nouvelle vérification.
        try
        {
            _fs.Copy(update.ExePath, newPath);
            if (_fs.Sha256(newPath) != update.Manifest.Sha256)
                throw new UpdateRejectedException("La copie de la mise à jour est abîmée (empreinte SHA-256 incorrecte).");
        }
        catch
        {
            TryDelete(newPath);
            throw;
        }

        // 2. L'exe en cours devient « .old ».
        try { _fs.Move(currentExe, oldPath); }
        catch
        {
            TryDelete(newPath);
            throw;
        }

        // 3. La nouvelle version prend sa place. En cas d'échec, on remet l'ancien exe.
        try
        {
            _fs.Move(newPath, currentExe);
        }
        catch (Exception e)
        {
            log.Error("Mise à jour : remplacement impossible, remise en place de l'ancienne version", e);
            try { _fs.Move(oldPath, currentExe); }
            catch (Exception e2) { log.Error($"Mise à jour : ANCIEN EXE À REMETTRE À LA MAIN : renommer {oldPath} en {currentExe}", e2); }
            TryDelete(newPath);
            throw;
        }
        log.Info($"Mise à jour : v{update.Version} en place (ancienne version gardée dans {oldPath} jusqu'au prochain lancement)");
    }

    /// <summary>
    /// Au lancement : supprime « .old » (ancienne version) et un éventuel « .new » abandonné.
    /// L'ancienne instance peut mettre un instant à se fermer : on réessaie pendant <paramref name="retryFor"/>.
    /// </summary>
    public bool CleanupLeftovers(string currentExe, TimeSpan retryFor)
    {
        var deadline = DateTime.UtcNow + retryFor;
        while (true)
        {
            var remaining = new[] { OldPath(currentExe), NewPath(currentExe) }.Where(_fs.Exists).ToList();
            foreach (var path in remaining)
            {
                try
                {
                    _fs.Delete(path);
                    log.Info($"Mise à jour : {Path.GetFileName(path)} supprimé");
                }
                catch { /* encore verrouillé */ }
            }
            if (!_fs.Exists(OldPath(currentExe)) && !_fs.Exists(NewPath(currentExe))) return true;
            if (DateTime.UtcNow >= deadline)
            {
                log.Warn($"Mise à jour : {OldPath(currentExe)} n'a pas pu être supprimé (réessai au prochain lancement)");
                return false;
            }
            Thread.Sleep(500);
        }
    }

    private void TryDelete(string path)
    {
        try { if (_fs.Exists(path)) _fs.Delete(path); } catch { /* sans gravité */ }
    }
}
