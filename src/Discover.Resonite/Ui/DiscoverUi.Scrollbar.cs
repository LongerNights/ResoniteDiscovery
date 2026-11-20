using Elements.Core;
using FrooxEngine;
using FrooxEngine.UIX;

namespace Discover.Resonite.Ui;

/// <summary>
/// A scrollbar down the right edge of the results area: a track you can press or drag, and a handle sized to the visible
/// share of the list. It reads and writes <c>ScrollRect.NormalizedPosition</c>; the card list is the only user.
/// </summary>
internal sealed partial class DiscoverUi
{
    private const float ScrollbarGutter = 22f;

    // NormalizedPosition.y is 0 at the top of the list (read from the live ScrollRect at rest on its first row, 2026-11-19).
    private const bool TopIsOne = false;

    private ScrollRect _scroll = null!;
    private RectTransform _barRect = null!;
    private RectTransform _handleRect = null!;
    private float _handleFraction = 1f;

    private void BuildScrollbar(RectTransform body)
    {
        var bar = body.Slot.AddSlot("Scrollbar");
        _barRect = bar.AttachComponent<RectTransform>();
        _barRect.AnchorMin.Value = new float2(1f, 0f);
        _barRect.AnchorMax.Value = new float2(1f, 1f);
        _barRect.OffsetMin.Value = new float2(-ScrollbarGutter + 6f, 8f);
        _barRect.OffsetMax.Value = new float2(-6f, -8f);

        var barUi = new UIBuilder(_barRect);
        RadiantUI_Constants.SetupDefaultStyle(barUi);
        var track = barUi.Image(Theme.Rail.SetA(0.6f));
        track.Slot.Name = "Track";
        track.Sprite.Target = _rounded; // rounded ends like the cards and panels
        track.NineSliceSizing.Value = NineSliceSizing.FixedSize;

        var handle = _barRect.Slot.AddSlot("Handle");
        _handleRect = handle.AttachComponent<RectTransform>();
        var image = handle.AttachComponent<Image>();
        image.Sprite.Target = _rounded;
        image.NineSliceSizing.Value = NineSliceSizing.FixedSize;
        image.Tint.Value = Theme.TextSecondary;
        _handleRect.OffsetMin.Value = new float2(2f, 0f); // inset in the track
        _handleRect.OffsetMax.Value = new float2(-2f, 0f);

        // The press target goes on top of the handle so it takes every press.
        var (_, press) = Theme.Catcher(barUi, "Press");
        // Buttons hand a drag over to the scroll area by default, which cancelled the bar's drag after a moment.
        press.PassThroughHorizontalMovement.Value = false;
        press.PassThroughVerticalMovement.Value = false;
        // Grey at rest, the dash's cyan accent while hovered or dragged, like the other controls.
        var hover = false;
        var held = false;
        void Tint() => image.Tint.Value = hover || held ? Theme.ButtonSelected : Theme.TextSecondary;
        press.LocalHoverEnter += (_, _) => { hover = true; Tint(); };
        press.LocalHoverLeave += (_, _) => { hover = false; Tint(); };
        press.LocalPressed += (_, _) => { held = true; Tint(); };
        press.LocalReleased += (_, _) => { held = false; Tint(); };
        press.LocalPressed += (_, e) => DragTo(e.localPoint.y);
        press.LocalPressing += (_, e) => DragTo(e.localPoint.y);
        UpdateScrollbar();
    }

    /// <summary>Puts the results list back at its first row (a new tab must not inherit the old tab's scroll position).</summary>
    public void ScrollToTop()
    {
        _scroll?.NormalizedPosition.Value = new float2(_scroll.NormalizedPosition.Value.x, TopIsOne ? 1f : 0f);
    }

    private void DragTo(float pointerY)
    {
        var rect = _barRect.ComputeGlobalComputeRect();
        if (rect.size.y < 1f)
        {
            return;
        }

        var fromTop = 1f - MathX.Clamp((pointerY - rect.position.y) / rect.size.y, 0f, 1f);
        var room = 1f - _handleFraction;
        var scroll = room > 0.001f ? MathX.Clamp((fromTop - _handleFraction * 0.5f) / room, 0f, 1f) : 0f;
        var pos = _scroll.NormalizedPosition.Value;
        _scroll.NormalizedPosition.Value = new float2(pos.x, TopIsOne ? 1f - scroll : scroll);
    }

    /// <summary>Sizes and places the handle from the list's height and scroll position; hides the bar when nothing scrolls.</summary>
    private void UpdateScrollbar()
    {
        if (_scroll is null || RectField is null
            || RectField.GetValue(_viewport) is not Rect view
            || RectField.GetValue(_content.GetComponent<RectTransform>()) is not Rect content)
        {
            return;
        }

        var scrollable = content.size.y > view.size.y + 1f && view.size.y > 1f;
        if (_barRect.Slot.ActiveSelf != scrollable)
        {
            _barRect.Slot.ActiveSelf = scrollable;
        }

        if (!scrollable)
        {
            return;
        }

        _handleFraction = MathX.Clamp(view.size.y / content.size.y, 0.08f, 1f);
        var y = _scroll.NormalizedPosition.Value.y;
        var fromTop = MathX.Clamp(TopIsOne ? 1f - y : y, 0f, 1f);
        var max = 1f - fromTop * (1f - _handleFraction);
        var min = max - _handleFraction;
        if (MathX.Abs(_handleRect.AnchorMax.Value.y - max) > 0.0005f || MathX.Abs(_handleRect.AnchorMin.Value.y - min) > 0.0005f)
        {
            _handleRect.AnchorMin.Value = new float2(0f, min);
            _handleRect.AnchorMax.Value = new float2(1f, max);
        }
    }
}
