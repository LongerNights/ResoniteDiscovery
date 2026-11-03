using System.Reflection;
using Discover.Core.Sources;

namespace Discover.Tests;

/// <summary>Spec testability requirement and M1 gate: Discover.Core has no engine reference.</summary>
public class CoreIsolationTests
{
    private static readonly string[] ForbiddenPrefixes = ["FrooxEngine", "SkyFrost", "Elements.", "Renderite", "ProtoFlux"];

    [Fact]
    public void CoreReferencesNoEngineAssemblies()
    {
        var core = typeof(ISessionSource).Assembly;
        var offenders = core.GetReferencedAssemblies()
            .Select(a => a.Name ?? "")
            .Where(name => ForbiddenPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        Assert.Empty(offenders);
    }
}
