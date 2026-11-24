using System.Reflection;

namespace Unpark.Core;

/// <summary>Nom, sous-titre et version de l'appli (la version vient de src/Directory.Build.props).</summary>
public static class AppInfo
{
    public const string Name = "Unpark";
    public const string Subtitle = "Optimisation PC gaming";

    /// <summary>Version courte, ex. « 0.1.0 ».</summary>
    public static string Version { get; } = ReadVersion(typeof(AppInfo).Assembly);

    public static string DisplayVersion => $"v{Version}";

    internal static string ReadVersion(Assembly assembly)
    {
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(info))
            return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        var plus = info.IndexOf('+');
        return plus >= 0 ? info[..plus] : info;
    }
}
