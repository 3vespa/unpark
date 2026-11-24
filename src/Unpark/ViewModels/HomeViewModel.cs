using System.Security.Principal;
using Microsoft.Win32;
using Unpark.Core;

namespace Unpark.ViewModels;

/// <summary>Accueil. À l'étape 1, sert surtout à vérifier que l'appli tourne bien en administrateur.</summary>
public sealed class HomeViewModel : PageViewModel
{
    public HomeViewModel() : base("Accueil", "")
    {
        IsAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        WindowsVersion = ReadWindowsVersion();
    }

    public string Welcome => $"Bienvenue dans {AppInfo.Name} {AppInfo.DisplayVersion}";
    public bool IsAdmin { get; }
    public string AdminText => IsAdmin ? "Oui, l'appli tourne en administrateur." : "Non : relance l'appli (clic droit → Exécuter en tant qu'administrateur).";
    public string WindowsVersion { get; }

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
