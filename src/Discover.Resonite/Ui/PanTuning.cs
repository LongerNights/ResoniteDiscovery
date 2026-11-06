using FrooxEngine;

namespace Discover.Resonite.Ui;

/// <summary>
/// The preview pan speed setting: every 360 preview's panner (the cards' and the detail panel's) registers here with the
/// engine's own defaults, and the multiplier scales them. Pooled cards live as long as the screen, so the setting applies
/// live to all of them.
/// </summary>
internal sealed class PanTuning
{
    private readonly List<(Perspective360Panner Panner, float Speed, float Acceleration)> _items = [];

    public float Multiplier { get; private set; } = 1f;

    /// <summary>Adds a panner. The scales keep a deliberately gentler panner gentler.</summary>
    public void Register(Perspective360Panner panner, float speedScale = 1f, float accelerationScale = 1f)
    {
        var entry = (panner, panner.HorizontalPanningSpeed.Value * speedScale, panner.HorizontalPanningAcceleration.Value * accelerationScale);
        _items.Add(entry);
        Apply(entry);
    }

    public void Set(float multiplier)
    {
        Multiplier = multiplier;
        _items.RemoveAll(i => i.Panner.IsRemoved);
        foreach (var item in _items)
        {
            Apply(item);
        }
    }

    private void Apply((Perspective360Panner Panner, float Speed, float Acceleration) item)
    {
        item.Panner.HorizontalPanningSpeed.Value = item.Speed * Multiplier;
        item.Panner.HorizontalPanningAcceleration.Value = item.Acceleration * Multiplier;
    }
}
