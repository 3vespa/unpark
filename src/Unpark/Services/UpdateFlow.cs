using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Unpark.Core.Updates;
using Unpark.ViewModels;

namespace Unpark.Services;

/// <summary>
/// Installation des mises à jour : au lancement (avant d'afficher la fenêtre) ou avec « Redémarrer maintenant ».
/// Le téléchargement et la vérification sont dans Unpark.Core (UpdateService), le remplacement de l'exe dans UpdateInstaller.
/// </summary>
public static class UpdateFlow
{
    public const string AfterUpdateArg = "--apres-maj";
    public const string FromFolderArg = "--maj-depuis";

    private static AppServices S => AppServices.Current;

    /// <summary>Version installée au lancement précédent (affichée dans le bandeau « Unpark a été mis à jour »), ou null.</summary>
    public static bool JustUpdated { get; private set; }

    /// <summary>Message d'erreur d'une installation ratée au lancement, à afficher dans le bandeau.</summary>
    public static string? StartupError { get; private set; }

    /// <summary>Mise à jour préparée depuis un dossier local (--maj-depuis), proposée dans le bandeau sans l'installer tout de suite.</summary>
    public static StagedUpdate? LocalStaged { get; private set; }

    private static string? CurrentExe => Environment.ProcessPath is { } p && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p : null;

    /// <summary>
    /// Au lancement, avant la fenêtre. true = la nouvelle version vient d'être lancée : cette instance doit se fermer sans rien afficher.
    /// </summary>
    public static bool RunAtStartup(string[] args)
    {
        var exe = CurrentExe;
        var afterUpdate = Array.FindIndex(args, a => a.Equals(AfterUpdateArg, StringComparison.OrdinalIgnoreCase));
        if (afterUpdate >= 0)
        {
            JustUpdated = true;
            // L'ancienne instance se ferme : on l'attend un peu pour ne pas travailler à deux en même temps.
            if (afterUpdate + 1 < args.Length && int.TryParse(args[afterUpdate + 1], out var pid)) WaitForExit(pid);
            S.Log.Info("Lancé après une mise à jour");
        }
        if (exe is not null)
        {
            // Supprime l'ancienne version (.old) en arrière-plan : elle peut être encore verrouillée quelques secondes.
            _ = Task.Run(() => S.UpdateInstaller.CleanupLeftovers(exe, TimeSpan.FromSeconds(30)));
        }

        var fromFolder = Array.FindIndex(args, a => a.Equals(FromFolderArg, StringComparison.OrdinalIgnoreCase));
        if (fromFolder >= 0)
        {
            // Test avant publication : mêmes vérifications qu'un téléchargement, puis bandeau « Redémarrer maintenant ».
            try
            {
                var folder = fromFolder + 1 < args.Length ? args[fromFolder + 1] : "";
                LocalStaged = S.Updates.StageFromFolder(folder);
            }
            catch (Exception e)
            {
                S.Log.Warn($"Mise à jour depuis un dossier refusée : {e.Message}");
                StartupError = $"Mise à jour refusée : {e.Message} Ta version actuelle n'a pas été modifiée.";
            }
            return false;
        }

        if (!S.Preferences.AutoUpdate)
        {
            S.Updates.DeleteStaged();
            return false;
        }

        var staged = S.Updates.FindStaged();
        if (staged is null || exe is null) return false;
        S.Log.Info($"Mise à jour v{staged.Version} en attente : installation avant l'ouverture");
        return InstallAndRelaunch(staged, exe, out var error) || Fail(error);

        static bool Fail(string? error)
        {
            StartupError = error;
            return false;
        }
    }

    /// <summary>Opération en cours qui interdit de redémarrer Unpark, ou null.</summary>
    public static string? RunningOperation()
    {
        if (S.IsBusy) return "une opération est en cours";
        if (DriveOptimizationViewModel.Running is not null) return "une optimisation de lecteur est en cours";
        return null;
    }

    /// <summary>« Redémarrer maintenant » : installe, relance la nouvelle version et ferme celle-ci. Message d'erreur sinon (rien n'a changé).</summary>
    public static string? RestartNow(StagedUpdate staged)
    {
        var exe = CurrentExe;
        if (exe is null) return "Emplacement de l'exe introuvable : la mise à jour s'installera à la prochaine ouverture.";
        if (!InstallAndRelaunch(staged, exe, out var error)) return error;
        System.Windows.Application.Current.Shutdown();
        return null;
    }

    private static bool InstallAndRelaunch(StagedUpdate staged, string exe, out string? error)
    {
        error = null;
        try
        {
            // Revérifie tout juste avant d'installer (signature et empreinte).
            var verified = S.Updates.FindStaged();
            if (verified is null || verified.Version != staged.Version)
                throw new UpdateRejectedException("Le fichier de mise à jour a changé depuis son téléchargement.");
            S.UpdateInstaller.Install(verified, exe);
        }
        catch (Exception e)
        {
            S.Log.Error("Mise à jour : installation impossible, version actuelle conservée", e);
            S.Updates.DeleteStaged();
            error = $"La mise à jour v{staged.Version} n'a pas pu être installée ({e.Message}). Ta version actuelle n'a pas été modifiée et continue de fonctionner.";
            return false;
        }

        S.Updates.DeleteStaged();
        try
        {
            Start(exe, $"{AfterUpdateArg} {Environment.ProcessId}");
            S.Log.Info($"Mise à jour : v{staged.Version} lancée, fermeture de l'ancienne version");
            return true;
        }
        catch (Exception e)
        {
            // La nouvelle version est en place mais n'a pas démarré : l'utilisateur la relance lui-même.
            S.Log.Error("Mise à jour : relance automatique impossible", e);
            Dialogs.Info($"Unpark v{staged.Version} est installé, mais n'a pas pu se relancer tout seul ({e.Message}).\n\nRouvre Unpark comme d'habitude.");
            return true;
        }
    }

    private static void Start(string exe, string arguments)
    {
        var info = new ProcessStartInfo(exe, arguments) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! };
        try
        {
            // Lancé depuis une appli administrateur : la nouvelle version hérite des droits, sans nouvelle demande.
            Process.Start(info);
        }
        catch (Win32Exception e) when (e.NativeErrorCode == 740) // élévation requise
        {
            info.UseShellExecute = true;
            Process.Start(info);
        }
    }

    private static void WaitForExit(int pid)
    {
        try
        {
            using var old = Process.GetProcessById(pid);
            old.WaitForExit(10_000);
        }
        catch { /* déjà fermée */ }
    }
}
