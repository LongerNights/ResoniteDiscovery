using Elements.Core;
using FrooxEngine;

namespace Discover.Resonite.Ui;

/// <summary>
/// The purple-and-black theme. Every colour is a <see cref="RadiantUI_Constants"/> member (CLAUDE.md conventions),
/// so it follows any future palette change; only the black background is a plain <see cref="colorX.Black"/>.
/// </summary>
internal static class Theme
{
    /// <summary>The dash tab colour: a soft purple (verification: the tab's label tint and selected colour).</summary>
    public static colorX Tab => RadiantUI_Constants.MidLight.PURPLE;

    public static colorX Background => colorX.Black.SetA(0.965f);
    public static colorX Rail => RadiantUI_Constants.Dark.PURPLE;
    public static colorX Card => RadiantUI_Constants.Dark.PURPLE;
    public static colorX Button => RadiantUI_Constants.Sub.PURPLE;
    public static colorX ButtonSelected => RadiantUI_Constants.MidLight.PURPLE;
    public static colorX Heading => RadiantUI_Constants.Hero.PURPLE;
    public static colorX Text => RadiantUI_Constants.TEXT_COLOR;
    public static colorX TextSecondary => RadiantUI_Constants.Neutrals.MIDLIGHT;
    /// <summary>The transition curtain over the main pane: opaque black, and the same black fully transparent.</summary>
    public static colorX CurtainShown => colorX.Black;
    public static colorX CurtainHidden => colorX.Black.SetA(0f);

    /// <summary>Labels laid over thumbnails: translucent black so any thumbnail stays readable underneath.</summary>
    public static colorX Pill => colorX.Black.SetA(0.7f);
}
