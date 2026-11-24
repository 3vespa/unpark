using Unpark.Core.Commands;
using Unpark.Core.Power;

namespace Unpark.Tests;

/// <summary>
/// Faux Windows pour l'alimentation : modèle en mémoire des plans, lu par IPowerApi et modifié par de fausses commandes powercfg.
/// Comprend les mêmes alias que le vrai powercfg pour les paramètres du catalogue.
/// </summary>
public sealed class FakePower : IPowerApi, ICommandRunner
{
    public sealed class Scheme
    {
        public string Name = "";
        public Dictionary<(Guid Sub, Guid Setting), (uint Ac, uint Dc)> Values = [];
    }

    public Dictionary<Guid, Scheme> Schemes { get; } = [];
    public Guid Active { get; set; }
    public HashSet<Guid> Hidden { get; } = [];
    public bool Battery { get; set; }
    /// <summary>Plans que Windows peut copier même s'ils ne sont pas listés (Performances optimales est caché).</summary>
    public Dictionary<Guid, Scheme> Templates { get; } = [];
    public HashSet<Guid> CannotDuplicate { get; } = [];
    public HashSet<Guid> MissingSettings { get; } = [];
    public List<string> Commands { get; } = [];

    public FakePower()
    {
        Schemes[PowerSettings.Balanced] = MakeScheme("Utilisation normale", ac: 5, dc: 5);
        Schemes[PowerSettings.HighPerformance] = MakeScheme("Performances élevées", ac: 50, dc: 50);
        Templates[PowerSettings.Ultimate] = MakeScheme("Performances optimales", ac: 77, dc: 77);
        Active = PowerSettings.Balanced;
        foreach (var d in PowerSettings.All.Where(d => d.HiddenByDefault)) Hidden.Add(d.Setting);
    }

    public Scheme MakeScheme(string name, uint ac, uint dc)
    {
        var s = new Scheme { Name = name };
        foreach (var d in PowerSettings.All)
            if (!MissingSettings.Contains(d.Setting)) s.Values[(d.SubGroup, d.Setting)] = (ac, dc);
        return s;
    }

    // IPowerApi
    public Guid GetActiveScheme() => Active;
    public IReadOnlyList<Guid> ListSchemes() => Schemes.Keys.ToList();
    public string? GetSchemeName(Guid scheme) => Schemes.TryGetValue(scheme, out var s) ? s.Name : null;
    public uint? ReadAc(Guid scheme, Guid sub, Guid setting) =>
        Schemes.TryGetValue(scheme, out var s) && s.Values.TryGetValue((sub, setting), out var v) ? v.Ac : null;
    public uint? ReadDc(Guid scheme, Guid sub, Guid setting) =>
        Schemes.TryGetValue(scheme, out var s) && s.Values.TryGetValue((sub, setting), out var v) ? v.Dc : null;
    public bool IsHidden(Guid sub, Guid setting) => Hidden.Contains(setting);
    public bool HasBattery() => Battery;

    // ICommandRunner : faux powercfg
    public CommandResult Run(string program, string arguments)
    {
        Commands.Add(arguments);
        var a = Split(arguments);
        switch (a[0].ToLowerInvariant())
        {
            case "/duplicatescheme":
            {
                var src = Guid.Parse(a[1]);
                var dest = Guid.Parse(a[2]);
                if (CannotDuplicate.Contains(src) || Schemes.ContainsKey(dest)) return Fail();
                var from = Schemes.GetValueOrDefault(src) ?? Templates.GetValueOrDefault(src);
                if (from is null) return Fail();
                Schemes[dest] = new Scheme { Name = from.Name, Values = new(from.Values) };
                return Ok();
            }
            case "/changename":
                Schemes[Guid.Parse(a[1])].Name = a[2];
                return Ok();
            case "/setacvalueindex":
            case "/setdcvalueindex":
            {
                var scheme = Schemes[Guid.Parse(a[1])];
                var key = (Resolve(a[2]), Resolve(a[3]));
                if (!scheme.Values.TryGetValue(key, out var v)) return Fail();
                var value = uint.Parse(a[4]);
                scheme.Values[key] = a[0] == "/setacvalueindex" ? (value, v.Dc) : (v.Ac, value);
                return Ok();
            }
            case "/setactive":
            {
                var g = Guid.Parse(a[1]);
                if (!Schemes.ContainsKey(g)) return Fail();
                Active = g;
                return Ok();
            }
            case "/delete":
            {
                var g = Guid.Parse(a[1]);
                if (g == Active) return Fail(); // Windows refuse de supprimer le plan actif
                return Schemes.Remove(g) ? Ok() : Fail();
            }
            case "-attributes":
            {
                var setting = Resolve(a[2]);
                if (a[3] == "-ATTRIB_HIDE") Hidden.Remove(setting); else Hidden.Add(setting);
                return Ok();
            }
            default:
                return Fail();
        }
    }

    private static Guid Resolve(string arg)
    {
        if (Guid.TryParse(arg, out var g)) return g;
        foreach (var d in PowerSettings.All)
        {
            if (d.Alias == arg) return d.Setting;
            if (d.SubAlias == arg) return d.SubGroup;
        }
        throw new ArgumentException($"Alias inconnu : {arg}");
    }

    private static string[] Split(string args)
    {
        var parts = new List<string>();
        var current = "";
        var quoted = false;
        foreach (var c in args)
        {
            if (c == '"') { quoted = !quoted; continue; }
            if (c == ' ' && !quoted) { if (current.Length > 0) parts.Add(current); current = ""; continue; }
            current += c;
        }
        if (current.Length > 0) parts.Add(current);
        return parts.ToArray();
    }

    private static CommandResult Ok() => new(0, "", "");
    private static CommandResult Fail() => new(1, "", "Paramètre non valide (simulé).");
}
