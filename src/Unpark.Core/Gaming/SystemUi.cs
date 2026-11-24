using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Unpark.Core.Gaming;

/// <summary>Accélération de la souris : seuils 1 et 2 + vitesse (0 = « Améliorer la précision du pointeur » désactivé).</summary>
public readonly record struct MouseParams(int Threshold1, int Threshold2, int Speed);

/// <summary>Un effet visuel de Windows, réglé par l'API documentée SystemParametersInfo (effet immédiat).</summary>
public sealed record VisualEffect(string Label, uint GetAction, uint SetAction, bool UsesUiParam = false, bool IsAnimationInfo = false);

/// <summary>Souris et effets visuels via SystemParametersInfo. Remplacé par une fausse version dans les tests.</summary>
public interface ISystemUiApi
{
    MouseParams GetMouse();
    /// <summary>Applique tout de suite et enregistre dans le profil (HKCU\Control Panel\Mouse).</summary>
    void SetMouse(MouseParams value);
    bool GetEffect(VisualEffect effect);
    void SetEffect(VisualEffect effect, bool on);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsSystemUiApi : ISystemUiApi
{
    private const uint SpiGetMouse = 0x0003;
    private const uint SpiSetMouse = 0x0004;
    private const uint SpifUpdateIniFile = 0x01;
    private const uint SpifSendChange = 0x02;

    public MouseParams GetMouse()
    {
        var values = new int[3];
        if (!SystemParametersInfo(SpiGetMouse, 0, values, 0)) throw new InvalidOperationException("Lecture des réglages de la souris impossible.");
        return new MouseParams(values[0], values[1], values[2]);
    }

    public void SetMouse(MouseParams value)
    {
        int[] values = [value.Threshold1, value.Threshold2, value.Speed];
        if (!SystemParametersInfo(SpiSetMouse, 0, values, SpifUpdateIniFile | SpifSendChange))
            throw new InvalidOperationException("Modification des réglages de la souris refusée par Windows.");
    }

    public bool GetEffect(VisualEffect effect)
    {
        if (effect.IsAnimationInfo)
        {
            var info = new AnimationInfo { Size = (uint)Marshal.SizeOf<AnimationInfo>() };
            if (!SystemParametersInfo(effect.GetAction, info.Size, ref info, 0)) throw Fail(effect);
            return info.MinAnimate != 0;
        }
        var on = 0;
        if (!SystemParametersInfo(effect.GetAction, 0, ref on, 0)) throw Fail(effect);
        return on != 0;
    }

    public void SetEffect(VisualEffect effect, bool on)
    {
        bool ok;
        if (effect.IsAnimationInfo)
        {
            var info = new AnimationInfo { Size = (uint)Marshal.SizeOf<AnimationInfo>(), MinAnimate = on ? 1 : 0 };
            ok = SystemParametersInfo(effect.SetAction, info.Size, ref info, SpifUpdateIniFile | SpifSendChange);
        }
        else if (effect.UsesUiParam)
            ok = SystemParametersInfo(effect.SetAction, on ? 1u : 0u, IntPtr.Zero, SpifUpdateIniFile | SpifSendChange);
        else
            ok = SystemParametersInfo(effect.SetAction, 0, on ? 1 : 0, SpifUpdateIniFile | SpifSendChange);
        if (!ok) throw Fail(effect);
    }

    private static InvalidOperationException Fail(VisualEffect e) => new($"Windows a refusé l'effet « {e.Label} ».");

    [StructLayout(LayoutKind.Sequential)]
    private struct AnimationInfo
    {
        public uint Size;
        public int MinAnimate;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint uiParam, int[] pvParam, uint winIni);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint uiParam, ref int pvParam, uint winIni);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint uiParam, ref AnimationInfo pvParam, uint winIni);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint uiParam, IntPtr pvParam, uint winIni);

    /// <summary>Pour les SPI_SETxxx qui reçoivent TRUE/FALSE directement dans pvParam.</summary>
    private static bool SystemParametersInfo(uint action, uint uiParam, int pvParam, uint winIni) =>
        SystemParametersInfo(action, uiParam, (IntPtr)pvParam, winIni);
}
