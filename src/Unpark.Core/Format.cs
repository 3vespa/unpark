using System.Globalization;

namespace Unpark.Core;

public static class Format
{
    private static readonly CultureInfo Fr = new("fr-FR");

    /// <summary>Taille lisible en français : « 0 octet », « 12 Ko », « 350 Mo », « 1,2 Go ».</summary>
    public static string Bytes(long bytes)
    {
        if (bytes < 1024) return bytes <= 1 ? $"{bytes} octet" : $"{bytes} octets";
        string[] units = ["Ko", "Mo", "Go", "To"];
        double value = bytes;
        var unit = -1;
        do { value /= 1024; unit++; } while (value >= 1024 && unit < units.Length - 1);
        return value < 10
            ? $"{value.ToString("0.#", Fr)} {units[unit]}"
            : $"{value.ToString("0", Fr)} {units[unit]}";
    }
}
