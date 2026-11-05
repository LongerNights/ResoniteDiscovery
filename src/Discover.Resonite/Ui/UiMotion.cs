using Elements.Core;
using FrooxEngine;

namespace Discover.Resonite.Ui;

/// <summary>
/// The one place animation timing lives: whether animations are on (Settings > Animations) and how fast they run
/// (Settings > Animation speed). Every duration in the UI goes through <see cref="Seconds"/>.
/// </summary>
internal static class UiMotion
{
    public static bool Enabled { get; set; } = true;

    /// <summary>1 is normal; above 1 is faster (durations are divided by it).</summary>
    public static float Speed { get; set; } = 1f;

    public static float Seconds(float normalSeconds) => normalSeconds / MathF.Max(0.2f, Speed);

    /// <summary>Fades a colour field from fully transparent to <paramref name="target"/>. Does nothing when animations are off.</summary>
    public static void FadeIn(Sync<colorX> color, colorX target, float normalSeconds = 0.28f)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            color.TweenFromTo(target.SetA(0f), target, Seconds(normalSeconds), CurvePreset.SineOut);
        }
        catch (Exception ex)
        {
            UniLog.Warning($"[Discover] Fade failed: {ex.GetType().Name}");
        }
    }
}
