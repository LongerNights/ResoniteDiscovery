#if RESONITE_RML
using System.Reflection;
using FrooxEngine;
using ResoniteModLoader;

namespace Discover.Resonite.Loader;

/// <summary>
/// The ResoniteModLoader shim (spec Integration §6): hands the engine to <see cref="DiscoverMod"/> and nothing else.
/// Built as a plain DLL into <c>rml_mods/</c>; this install loads it through MonkeyLoader's RML compatibility pack.
/// Other loaders would get their own shim in this folder from the same project (<c>ResoniteTargetModLoader</c>).
/// </summary>
public sealed class GalaxySieveMod : ResoniteMod
{
    public override string Name => "Galaxy Sieve";

    public override string Author => "Winter";

    public override string Version => typeof(GalaxySieveMod).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    public override void OnEngineInit()
    {
        // Engine.Cloud and the userspace world are not ready this early; start once the engine has finished initializing.
        var engine = Engine.Current;
        engine.OnShutdown += DiscoverMod.Stop;
        engine.RunPostInit(() => DiscoverMod.Start(engine));
    }
}
#endif
