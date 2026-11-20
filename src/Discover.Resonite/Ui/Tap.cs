using Elements.Core;
using FrooxEngine;
using FrooxEngine.UIX;

namespace Discover.Resonite.Ui;

/// <summary>
/// Click handling for buttons inside a scrolling list. The engine runs a button's Pressed event the moment the pointer goes
/// down (decompiled Button.OnPressBegin), so a press that turns into a scroll drag had already "clicked". A tap here fires
/// on release, and only if the pointer stayed within <see cref="Slop"/> canvas units of where it went down.
/// </summary>
internal static class Tap
{
    /// <summary>How far (canvas units) the pointer may move between press and release and still count as a click.</summary>
    public const float Slop = 8f;

    public static void OnTap(this Button button, ButtonEventHandler handler)
    {
        var down = float2.Zero;
        var armed = false;
        var moved = false;

        button.LocalPressed += (_, e) =>
        {
            down = e.localPoint;
            armed = true;
            moved = false;
        };
        button.LocalPressing += (_, e) =>
        {
            if (armed && MathX.Distance(e.localPoint, down) > Slop)
            {
                moved = true;
            }
        };
        button.LocalReleased += (sender, e) =>
        {
            var click = armed && !moved && MathX.Distance(e.localPoint, down) <= Slop;
            armed = false;
            if (click)
            {
                handler(sender, e);
            }
        };
    }
}
