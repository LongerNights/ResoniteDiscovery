using Elements.Core;
using FrooxEngine;
using FrooxEngine.Store;

namespace Discover.Resonite.Ui;

/// <summary>
/// Puts the embedded galaxy icon into the local asset database and returns its <c>local://</c> URI.
/// The dash button reads <c>RadiantDashScreen.Icon</c> once, when the button is built (RadiantDashButton.Setup),
/// so the controller waits for this before attaching the screen. <c>LocalDB.ImportLocalAssetAsync</c> dedupes by
/// content hash, so importing the same file every launch returns the same URI and stores nothing new.
/// Returns null on any failure; the caller falls back to the stock Worlds icon.
/// </summary>
internal static class IconLoader
{
    private const string ResourceName = "Discover.Resonite.Assets.galaxy-icon.png";

    public static async Task<Uri?> ImportAsync(Engine engine)
    {
        try
        {
            await using var resource = typeof(IconLoader).Assembly.GetManifestResourceStream(ResourceName);
            if (resource is null)
            {
                UniLog.Warning("[Discover] Icon resource missing; using the Worlds icon");
                return null;
            }

            var folder = Path.Combine(Path.GetTempPath(), "ResoniteDiscovery");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "galaxy-icon.png");
            await using (var file = File.Create(path))
            {
                await resource.CopyToAsync(file);
            }

            return await engine.LocalDB.ImportLocalAssetAsync(path, LocalDB.ImportLocation.Copy);
        }
        catch (Exception ex)
        {
            UniLog.Warning($"[Discover] Icon import failed ({ex.GetType().Name}); using the Worlds icon");
            return null;
        }
    }
}
