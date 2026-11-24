using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;

namespace Unpark.Core.Commands;

public sealed record CommandResult(int ExitCode, string Output, string Error)
{
    public bool Ok => ExitCode == 0;
    public string Text => string.Join(" ", new[] { Output, Error }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
}

/// <summary>Lance une commande Windows (powercfg…). Remplacé par une fausse version dans les tests.</summary>
public interface ICommandRunner
{
    CommandResult Run(string program, string arguments);
}

/// <summary>Lance un programme sans fenêtre, attend sa fin (60 s maximum) et récupère sa sortie.</summary>
[SupportedOSPlatform("windows")]
public sealed class ProcessCommandRunner : ICommandRunner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    static ProcessCommandRunner()
    {
        // Les outils en ligne de commande écrivent dans la page de code OEM (ex. 850) : accents lisibles dans le journal.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public CommandResult Run(string program, string arguments)
    {
        var oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        var psi = new ProcessStartInfo(program, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = oem,
            StandardErrorEncoding = oem,
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Impossible de lancer {program}.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(Timeout))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return new CommandResult(-1, "", $"{program} n'a pas répondu en {Timeout.TotalSeconds} s.");
        }
        return new CommandResult(process.ExitCode, output.Result.Trim(), error.Result.Trim());
    }
}
