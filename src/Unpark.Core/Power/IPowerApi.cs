namespace Unpark.Core.Power;

/// <summary>
/// Lecture de l'état de l'alimentation (indépendante de la langue de Windows).
/// Les modifications passent par powercfg (visibles dans le journal). Remplacé par une fausse version dans les tests.
/// </summary>
public interface IPowerApi
{
    Guid GetActiveScheme();
    IReadOnlyList<Guid> ListSchemes();
    string? GetSchemeName(Guid scheme);
    /// <summary>Valeur sur secteur, ou null si le paramètre n'existe pas sur ce PC.</summary>
    uint? ReadAc(Guid scheme, Guid subGroup, Guid setting);
    /// <summary>Valeur sur batterie, ou null si le paramètre n'existe pas sur ce PC.</summary>
    uint? ReadDc(Guid scheme, Guid subGroup, Guid setting);
    /// <summary>true si le paramètre est caché dans les options d'alimentation.</summary>
    bool IsHidden(Guid subGroup, Guid setting);
    /// <summary>true si le PC a une batterie (portable).</summary>
    bool HasBattery();
}
