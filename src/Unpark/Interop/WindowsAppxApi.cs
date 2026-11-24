using Unpark.Core.Debloat;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace Unpark.Interop;

/// <summary>Applis Windows via l'API PackageManager (Windows 10 et 11), pour l'utilisateur courant seulement.</summary>
public sealed class WindowsAppxApi : IAppxApi
{
    private readonly PackageManager _manager = new();

    public IReadOnlyList<AppxPackageInfo> ListForCurrentUser()
    {
        var list = new List<AppxPackageInfo>();
        foreach (var p in _manager.FindPackagesForUser(string.Empty))
        {
            try
            {
                string display;
                try { display = p.DisplayName; } catch { display = ""; }
                list.Add(new AppxPackageInfo(
                    p.Id.Name, p.Id.FamilyName, p.Id.FullName, display,
                    p.IsFramework, p.IsResourcePackage, p.SignatureKind == PackageSignatureKind.System));
            }
            catch
            {
                // Paquet illisible : ignoré.
            }
        }
        return list;
    }

    public void Remove(string fullName)
    {
        var result = _manager.RemovePackageAsync(fullName).AsTask().GetAwaiter().GetResult();
        if (result.ExtendedErrorCode is { } error)
            throw new InvalidOperationException($"{result.ErrorText} (0x{error.HResult:X8})");
    }
}
