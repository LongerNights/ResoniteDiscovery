using Elements.Core;
using FrooxEngine;
using FrooxEngine.UIX;

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

    // The panner sets AngleOffset.y toward ((pointer - rect.position) / rect.size - 0.5) * 2 * VerticalRange, where rect is
    // the button's CurrentGlobalRect (decompiled Perspective360Panner, Button, InteractionElement). In the scrolling card
    // list that stored rect is offset from where the card really is by an amount that depends on the card's row (measured
    // 2026-11-08 from the hover log: the pointer was inside RectTransform.ComputeGlobalComputeRect in 400 of 400 samples, but
    // inside CurrentGlobalRect in only 53), so the panner's vertical angle ran to hundreds of degrees and flipped the picture.
    // The panner is kept for the sideways pan and the zoom; the vertical angle is worked out here from the computed rect.

    private const double HoverTimeout = 0.1;

    /// <summary>
    /// Adds a panner and feeds its pan and zoom into <paramref name="block"/>, with the vertical tilt taken from the pointer
    /// over <paramref name="button"/>. The scales keep a deliberately gentler panner gentler.
    /// </summary>
    public void Register(Perspective360Panner panner, Projection360PropertyBlock block, Button button, float speedScale = 1f, float accelerationScale = 1f)
    {
        var entry = (panner, panner.HorizontalPanningSpeed.Value * speedScale, panner.HorizontalPanningAcceleration.Value * accelerationScale);
        _items.Add(entry);
        Apply(entry);

        block.PerspectiveFieldOfView.DriveFrom(panner.FOV);

        var target = 0f;
        var tilt = 0f;
        var lastHover = double.NegativeInfinity;
        var lastTick = double.NegativeInfinity;

        void Update()
        {
            var now = panner.Time.WorldTime;
            var dt = (float)(now - lastTick);
            if (dt > 0f)
            {
                lastTick = now;
                var goal = now - lastHover < HoverTimeout ? target : 0f;
                tilt += (goal - tilt) * (1f - MathX.Exp(-Math.Min(dt, 0.1f) * panner.VerticalPanningSpeed.Value * 2f));
            }

            block.PerspectiveAngleOffset.Value = new float2(panner.AngleOffset.Value.x, tilt);
        }

        button.LocalHoverStay += (_, e) =>
        {
            var rect = button.RectTransform.ComputeGlobalComputeRect();
            var y = rect.size.y > 0.001f ? (e.localPoint.y - rect.position.y) / rect.size.y : 0.5f;
            var range = Math.Max(0f, panner.VerticalRange.Value);
            target = MathX.Clamp((y - 0.5f) * 2f, -1f, 1f) * range;
            lastHover = panner.Time.WorldTime;
            Update();
        };

        panner.AngleOffset.Changed += _ => Update();
        panner.FOV.Changed += _ => Update();
        Update();
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
