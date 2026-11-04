using FrooxEngine;

namespace Discover.Resonite;

/// <summary>
/// Replaces the stock Worlds tab with ours at runtime only (spec Integration §2, Option B). The one write it makes to
/// anything it did not create is the stock tab button's <c>ActiveSelf</c> (a slot the dash builds at runtime, outside the
/// saved screens workspace, V29) and <c>RadiantDash.CurrentScreen</c>, the same field a tab-button press sets. Everything
/// is undone by <see cref="Restore"/>, and nothing is persisted, so a crash or uninstall leaves the stock tab as the
/// engine builds it on the next launch (F25).
/// </summary>
internal sealed class TabTakeover
{
    private readonly RadiantDash _dash;
    private RadiantDashScreen? _stock;
    private Slot? _hidden;
    private bool _classic;

    public TabTakeover(RadiantDash dash)
    {
        _dash = dash;
    }

    /// <summary>True while the user has pressed Classic browser and has not yet picked another tab.</summary>
    public bool ClassicMode => _classic;

    /// <summary>
    /// Called every tick. With <paramref name="takeOver"/> on (our UI built, not paused, setting on) it hides the stock
    /// button, keeps it hidden and moves the user off the stock screen; with it off it restores the stock button.
    /// </summary>
    public void Tick(RadiantDashScreen ours, bool takeOver)
    {
        if (!takeOver)
        {
            Restore();
            return;
        }

        var stock = FindStock();
        if (stock is null)
        {
            return;
        }

        var button = FindButton(stock);
        if (button is not null)
        {
            if (button.ActiveSelf)
            {
                button.ActiveSelf = false; // also re-hides a button the dash rebuilt
            }

            _hidden = button;
        }

        var current = _dash.CurrentScreen.Target;
        if (current != stock)
        {
            _classic = false;
        }
        else if (!_classic)
        {
            _dash.CurrentScreen.Target = ours; // the stock tab was open at startup or opened by something else
        }
    }

    /// <summary>Shows the stock Worlds screen (its button stays hidden) until the user picks another tab.</summary>
    public bool OpenClassic()
    {
        var stock = FindStock();
        if (stock is null)
        {
            return false;
        }

        _classic = true;
        _dash.CurrentScreen.Target = stock;
        return true;
    }

    /// <summary>Makes the stock Worlds button visible again. Safe to call at any time.</summary>
    public void Restore()
    {
        _classic = false;
        if (_hidden is { IsRemoved: false } slot)
        {
            slot.ActiveSelf = true;
        }

        _hidden = null;
    }

    private RadiantDashScreen? FindStock()
    {
        if (_stock is { IsRemoved: false })
        {
            return _stock;
        }

        _stock = _dash.GetScreen<GridContainerScreen>(g => g.HasPreset(typeof(WorldsScreenInitializer)));
        return _stock;
    }

    private Slot? FindButton(RadiantDashScreen stock)
    {
        if (_hidden is { IsRemoved: false } known)
        {
            return known;
        }

        // Not dash.ButtonsRoot: that is the overlay's Buttons slot, not the dash tab strip (found in the first in-game test).
        var buttons = _dash.Slot.GetComponentsInChildren<RadiantDashButton>(b => b.Screen.Target == stock);
        return buttons.FirstOrDefault()?.Slot;
    }
}
