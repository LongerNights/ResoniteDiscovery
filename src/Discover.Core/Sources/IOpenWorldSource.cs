using Discover.Core.Model;

namespace Discover.Core.Sources;

/// <summary>
/// The worlds the user has open, backed in game by <c>Engine.WorldManager</c>. Pulled on the update thread; no
/// requests are involved (spec networking budget).
/// </summary>
public interface IOpenWorldSource
{
    /// <summary>Every open world except userspace, in no particular order.</summary>
    IReadOnlyList<OpenWorldSnapshot> GetOpenWorlds();

    /// <summary>How many worlds <see cref="GetOpenWorlds"/> would list, without building their user lists.</summary>
    int CountOpenWorlds();
}
