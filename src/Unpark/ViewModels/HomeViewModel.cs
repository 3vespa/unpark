using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Unpark.Core;
using Unpark.Services;

namespace Unpark.ViewModels;

/// <summary>Accueil : état administrateur, sécurité (point de restauration, tout annuler, journaux) et réglage de test.</summary>
public sealed partial class HomeViewModel : PageViewModel
{
    private static AppServices S => AppServices.Current;

    public HomeViewModel() : base("Accueil", "")
    {
        IsAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        WindowsVersion = ReadWindowsVersion();
        BackupWarning = S.Backup.LoadWarning;
        S.StateChanged += Refresh;
        Refresh();
        _ = LoadRestorePointAsync();
    }

    public string Welcome => $"Bienvenue dans {AppInfo.Name} {AppInfo.DisplayVersion}";
    public bool IsAdmin { get; }
    public string AdminText => IsAdmin ? "Oui, l'appli tourne en administrateur." : "Non : relance l'appli (clic droit → Exécuter en tant qu'administrateur).";
    public string WindowsVersion { get; }
    public string? BackupWarning { get; }
    public bool HasBackupWarning => BackupWarning is not null;

    [ObservableProperty] private string _restorePointText = "Recherche…";
    [ObservableProperty] private string _activeChangesText = "";
    [ObservableProperty] private string _demoStateText = "";
    [ObservableProperty] private bool _demoApplied;

    private void Refresh()
    {
        var count = S.Backup.Entries.Count;
        ActiveChangesText = count == 0
            ? "Aucun réglage modifié par Unpark : ton PC est dans son état d'origine."
            : $"{count} réglage(s) modifié(s) par Unpark. « Tout annuler » remet l'état d'origine exact.";
        DemoApplied = S.Demo.IsApplied();
        DemoStateText = DemoApplied ? "Activé (valeur écrite, sauvegarde faite)" : "Désactivé";
        _ = LoadRestorePointAsync();
    }

    private async Task LoadRestorePointAsync()
    {
        var latest = await Task.Run(S.RestorePoints.GetLatestUtc);
        RestorePointText = latest is { } d
            ? $"Dernier point de restauration : {d.ToLocalTime():dd/MM/yyyy à HH:mm}"
            : "Aucun point de restauration trouvé (ou protection du système désactivée).";
    }

    [RelayCommand]
    private Task CreateRestorePointAsync() => SafetyFlow.CreateRestorePointAsync();

    [RelayCommand]
    private Task RevertAllAsync() => SafetyFlow.RevertAllAsync();

    [RelayCommand]
    private Task ApplyDemoAsync() => SafetyFlow.ApplyAsync(S.Demo);

    [RelayCommand]
    private Task RevertDemoAsync() => SafetyFlow.RevertAsync(S.Demo);

    [RelayCommand]
    private void OpenLogs() => OpenFolder(S.Paths.LogsDir);

    [RelayCommand]
    private void OpenBackupFolder() => OpenFolder(S.Paths.Root);

    private static void OpenFolder(string dir)
    {
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }

    /// <summary>
    /// Nom lisible de Windows. Attention : la clé ProductName indique « Windows 10 » même sur Windows 11,
    /// on se fie donc au numéro de build (22000 et plus = Windows 11).
    /// </summary>
    private static string ReadWindowsVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var build = int.TryParse(key?.GetValue("CurrentBuild") as string, out var b) ? b : Environment.OSVersion.Version.Build;
            var ubr = key?.GetValue("UBR") is int u ? $".{u}" : "";
            var display = key?.GetValue("DisplayVersion") as string ?? "";
            var name = build >= 22000 ? "Windows 11" : "Windows 10";
            return $"{name} {display} (build {build}{ubr})".Replace("  ", " ");
        }
        catch
        {
            return Environment.OSVersion.VersionString;
        }
    }
}
