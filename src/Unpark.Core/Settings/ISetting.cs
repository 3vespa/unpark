using Unpark.Core.Backup;
using Unpark.Core.Logging;
using Unpark.Core.Registry;

namespace Unpark.Core.Settings;

/// <summary>
/// Un réglage réversible. Règle d'or : Apply() sauvegarde la valeur d'origine AVANT de modifier quoi que ce soit,
/// et Revert() remet exactement cette valeur (jamais une valeur par défaut supposée).
/// </summary>
public interface ISetting
{
    /// <summary>Identifiant stable (sert de clé dans backup.json : ne jamais le renommer).</summary>
    string Id { get; }
    string Name { get; }
    string Description { get; }
    bool RequiresRestart { get; }

    /// <summary>Le réglage est-il déjà dans l'état voulu ?</summary>
    bool IsApplied();
    void Apply();
    /// <summary>Remet l'état sauvegardé. Ne fait rien s'il n'y a pas de sauvegarde.</summary>
    void Revert();
}

/// <summary>Services dont les réglages ont besoin (remplacés par des fausses versions dans les tests).</summary>
public sealed record SettingContext(IRegistry Registry, BackupStore Backup, IActionLog Log);
