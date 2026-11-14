using System;
using Android.Content;
using Android.OS;
using Android.Views;

namespace FnrPort.Droid;

/// <summary>
/// Vibración (el juego no tiene: el port la añade). Vibra el motor del mando que se está usando si
/// lo tiene; si no, el de la consola. La llama FnrPort.Rumble desde el hilo del juego.
/// </summary>
static class Haptics
{
    static Vibrator sSystem;
    static volatile Vibrator sPad;
    static int sPadDevice = int.MinValue;

    public static void Init(Context ctx)
    {
        if (sSystem != null) return;
        try
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.S)
                sSystem = ((VibratorManager)ctx.GetSystemService(Context.VibratorManagerService))?.DefaultVibrator;
            else
#pragma warning disable CA1422
                sSystem = (Vibrator)ctx.GetSystemService(Context.VibratorService);
#pragma warning restore CA1422
        }
        catch (Exception ex) { Log.Write("Vibración: " + ex.Message); }
        Log.Write("Vibración de la consola: " + (sSystem?.HasVibrator == true ? "sí" : "no"));
    }

    /// <summary>Último mando usado: si tiene motor, vibra él.</summary>
    public static void UsePad(InputDevice d)
    {
        if (d == null || d.Id == sPadDevice) return;
        sPadDevice = d.Id;
        Vibrator v = null;
        try
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.S) v = d.VibratorManager?.DefaultVibrator;
#pragma warning disable CA1422
            if (v == null || !v.HasVibrator) v = d.Vibrator;
#pragma warning restore CA1422
        }
        catch { }
        sPad = v != null && v.HasVibrator ? v : null;
        Log.Write($"Mando en uso: {d.Name} (vibración propia: {(sPad != null ? "sí" : "no")})");
    }

    /// <summary>Vibra ms milisegundos con intensidad 1-255 (ya escalada por el ajuste del launcher).</summary>
    public static void Vibrate(int ms, int amp)
    {
        var v = sPad ?? sSystem;
        if (v == null || !v.HasVibrator) return;
        try
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                bool ampCtl = v.HasAmplitudeControl;
                // sin control de intensidad, la fuerza se nota en la duración
                int d = ampCtl ? ms : Math.Max(12, ms * Math.Clamp(amp, 1, 255) / 255);
                v.Vibrate(VibrationEffect.CreateOneShot(d, ampCtl ? Math.Clamp(amp, 1, 255) : VibrationEffect.DefaultAmplitude));
            }
            else
#pragma warning disable CA1422
                v.Vibrate(ms);
#pragma warning restore CA1422
        }
        catch { }
    }
}
