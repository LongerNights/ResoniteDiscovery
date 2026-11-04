using Elements.Core;
using FrooxEngine;

namespace Discover.Resonite.Ui;

/// <summary>
/// The look of the stock Worlds tab (read from the live stock screen, 2026-11-04): the neutral Dark and Mid greys, the
/// cyan accent the dash uses for the selected tab, and the tiled background texture under a Dark tint at 96.5 %.
/// Every colour is a <see cref="RadiantUI_Constants"/> member (CLAUDE.md conventions), so it follows any future palette change.
/// </summary>
/// <summary>
/// The fixed type scale (docs/ui-redesign.md). Text uses one of these sizes instead of shrinking to fit its label, so
/// the same kind of text is the same size everywhere; a label that is too long is shortened, not shrunk.
/// </summary>
internal static class TypeScale
{
    public const float Caption = 14f;
    public const float Body = 18f;
    public const float Label = 22f;
    public const float Title = 28f;
    public const float Hero = 36f;

    private static readonly float[] Steps = [Caption, Body, Label, Title, Hero];

    /// <summary>The scale step closest to the middle of an old auto-size range (ties go to the smaller step).</summary>
    public static float Snap(float min, float max)
    {
        var middle = (min + max) * 0.5f;
        var best = Steps[0];
        foreach (var step in Steps)
        {
            if (MathF.Abs(step - middle) < MathF.Abs(best - middle) - 0.001f)
            {
                best = step;
            }
        }

        return best;
    }
}

internal static class Theme
{
    /// <summary>The dash tab colour: the accent the dash uses for the selected tab.</summary>
    public static colorX Tab => RadiantUI_Constants.Hero.CYAN;

    /// <summary>The tint of the tiled background texture: the stock tab's own value (Dark at 96.5 %), so the world just shows through.</summary>
    public static colorX Background => RadiantUI_Constants.Neutrals.DARK.SetA(0.965f);

    /// <summary>The left rail: a little lighter than the pane, see-through like it.</summary>
    public static colorX RailBackground => RadiantUI_Constants.Neutrals.MID.SetA(0.35f);

    /// <summary>Popover and dialog panels: solid Dark.</summary>
    public static colorX Rail => RadiantUI_Constants.Neutrals.DARK;
    public static colorX Card => RadiantUI_Constants.Neutrals.MID;

    /// <summary>Buttons: the stock Mid grey. The selected and primary ones are the dash's cyan accent.</summary>
    public static colorX Button => RadiantUI_Constants.Neutrals.MID;
    public static colorX ButtonSelected => RadiantUI_Constants.MidLight.CYAN;
    public static colorX Heading => RadiantUI_Constants.Neutrals.LIGHT;
    public static colorX Text => RadiantUI_Constants.TEXT_COLOR;
    public static colorX TextSecondary => RadiantUI_Constants.Neutrals.MIDLIGHT;
    /// <summary>Chips: excluded tags read red, the user's own tags orange, per the design.</summary>
    public static colorX ChipRequired => RadiantUI_Constants.MidLight.CYAN;
    public static colorX ChipExcluded => RadiantUI_Constants.MidLight.RED;
    public static colorX ChipPersonal => RadiantUI_Constants.MidLight.ORANGE;

    /// <summary>A starred world's star.</summary>
    public static colorX Favorite => RadiantUI_Constants.Hero.YELLOW;

    /// <summary>A disabled or secondary button.</summary>
    public static colorX ButtonQuiet => RadiantUI_Constants.Neutrals.MID.SetA(0.55f);

    /// <summary>Behind a popover: a click-catcher that does not dim anything.</summary>
    public static colorX Scrim => colorX.Black.SetA(0.02f);

    /// <summary>Behind the join confirmation: a gentle dim, as a modal expects.</summary>
    public static colorX ConfirmScrim => colorX.Black.SetA(0.55f);

    /// <summary>Error banner.</summary>
    public static colorX Banner => RadiantUI_Constants.Sub.RED;

    /// <summary>The transition curtain over the main pane: opaque black, and the same black fully transparent.</summary>
    public static colorX CurtainShown => colorX.Black;
    public static colorX CurtainHidden => colorX.Black.SetA(0f);

    /// <summary>Labels laid over thumbnails: translucent black so any thumbnail stays readable underneath.</summary>
    public static colorX Pill => colorX.Black.SetA(0.7f);
}
