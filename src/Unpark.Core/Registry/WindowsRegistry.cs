using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Unpark.Core.Registry;

/// <summary>Le vrai registre Windows, vue 64 bits (l'appli est 64 bits).</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRegistry : IRegistry
{
    private static RegistryKey Root(RegHive hive) =>
        RegistryKey.OpenBaseKey(hive == RegHive.CurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, RegistryView.Registry64);

    public RegValue? Get(RegPath path)
    {
        using var root = Root(path.Hive);
        using var key = root.OpenSubKey(path.Key);
        if (key is null) return null;
        var data = key.GetValue(path.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (data is null) return null;
        return key.GetValueKind(path.Name) switch
        {
            RegistryValueKind.DWord => RegValue.DWord(unchecked((uint)(int)data)),
            RegistryValueKind.QWord => RegValue.QWord((long)data),
            RegistryValueKind.String => RegValue.String((string)data),
            RegistryValueKind.ExpandString => RegValue.ExpandString((string)data),
            RegistryValueKind.MultiString => RegValue.MultiString((string[])data),
            RegistryValueKind.Binary => RegValue.Binary((byte[])data),
            var other => throw new NotSupportedException($"Type de valeur non géré : {other} ({path})."),
        };
    }

    public void Set(RegPath path, RegValue value)
    {
        using var root = Root(path.Hive);
        using var key = root.CreateSubKey(path.Key, writable: true);
        switch (value.Kind)
        {
            case RegKind.DWord: key.SetValue(path.Name, unchecked((int)(uint)value.Number), RegistryValueKind.DWord); break;
            case RegKind.QWord: key.SetValue(path.Name, value.Number, RegistryValueKind.QWord); break;
            case RegKind.String: key.SetValue(path.Name, value.Text ?? "", RegistryValueKind.String); break;
            case RegKind.ExpandString: key.SetValue(path.Name, value.Text ?? "", RegistryValueKind.ExpandString); break;
            case RegKind.MultiString: key.SetValue(path.Name, value.Lines ?? [], RegistryValueKind.MultiString); break;
            case RegKind.Binary: key.SetValue(path.Name, value.Bytes ?? [], RegistryValueKind.Binary); break;
        }
    }

    public void Delete(RegPath path)
    {
        using var root = Root(path.Hive);
        using var key = root.OpenSubKey(path.Key, writable: true);
        key?.DeleteValue(path.Name, throwOnMissingValue: false);
    }
}
