namespace Unpark.Core.Registry;

public enum RegHive
{
    CurrentUser,
    LocalMachine,
}

public enum RegKind
{
    DWord,
    QWord,
    String,
    ExpandString,
    MultiString,
    Binary,
}

/// <summary>Une valeur du registre (type + donnée), sérialisable en JSON pour la sauvegarde.</summary>
public sealed record RegValue
{
    public RegKind Kind { get; init; }
    public long Number { get; init; }
    public string? Text { get; init; }
    public string[]? Lines { get; init; }
    public byte[]? Bytes { get; init; }

    public static RegValue DWord(int value) => new() { Kind = RegKind.DWord, Number = (uint)value };
    public static RegValue DWord(uint value) => new() { Kind = RegKind.DWord, Number = value };
    public static RegValue QWord(long value) => new() { Kind = RegKind.QWord, Number = value };
    public static RegValue String(string value) => new() { Kind = RegKind.String, Text = value };
    public static RegValue ExpandString(string value) => new() { Kind = RegKind.ExpandString, Text = value };
    public static RegValue MultiString(string[] value) => new() { Kind = RegKind.MultiString, Lines = value };
    public static RegValue Binary(byte[] value) => new() { Kind = RegKind.Binary, Bytes = value };

    /// <summary>Même type et même donnée (comparaison du contenu des tableaux).</summary>
    public bool SameAs(RegValue? other) =>
        other is not null && Kind == other.Kind && Kind switch
        {
            RegKind.DWord or RegKind.QWord => Number == other.Number,
            RegKind.String or RegKind.ExpandString => Text == other.Text,
            RegKind.MultiString => (Lines ?? []).SequenceEqual(other.Lines ?? []),
            RegKind.Binary => (Bytes ?? []).AsSpan().SequenceEqual(other.Bytes ?? []),
            _ => false,
        };

    public override string ToString() => Kind switch
    {
        RegKind.DWord or RegKind.QWord => $"{Number} ({Kind})",
        RegKind.String or RegKind.ExpandString => $"\"{Text}\" ({Kind})",
        RegKind.MultiString => $"[{string.Join(", ", Lines ?? [])}] ({Kind})",
        RegKind.Binary => $"{Convert.ToHexString(Bytes ?? [])} ({Kind})",
        _ => Kind.ToString(),
    };
}

/// <summary>Emplacement d'une valeur : ruche + chemin de clé + nom de valeur.</summary>
public sealed record RegPath(RegHive Hive, string Key, string Name)
{
    public override string ToString() => $"{(Hive == RegHive.CurrentUser ? "HKCU" : "HKLM")}\\{Key}\\{Name}";

    /// <summary>Identifiant utilisé dans la sauvegarde : « reg:HKCU\chemin\nom ».</summary>
    public string BackupKey => $"reg:{this}";

    public static RegPath? FromBackupKey(string key)
    {
        if (!key.StartsWith("reg:", StringComparison.Ordinal)) return null;
        var s = key[4..];
        RegHive hive;
        if (s.StartsWith("HKCU\\", StringComparison.Ordinal)) hive = RegHive.CurrentUser;
        else if (s.StartsWith("HKLM\\", StringComparison.Ordinal)) hive = RegHive.LocalMachine;
        else return null;
        var rest = s[5..];
        var last = rest.LastIndexOf('\\');
        return last <= 0 ? null : new RegPath(hive, rest[..last], rest[(last + 1)..]);
    }
}

/// <summary>Accès au registre. Remplacé par une fausse version dans les tests.</summary>
public interface IRegistry
{
    /// <summary>La valeur, ou null si elle n'existe pas.</summary>
    RegValue? Get(RegPath path);
    void Set(RegPath path, RegValue value);
    /// <summary>Supprime la valeur (sans erreur si elle n'existe pas).</summary>
    void Delete(RegPath path);
}
