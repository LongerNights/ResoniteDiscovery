using Elements.Core;
using FrooxEngine;
using System.Collections.Concurrent;

namespace Discover.Resonite.Services;

/// <summary>
/// Turns the name in an <c>author:name</c> search term into a user id through the engine's own user lookup
/// (<c>Cloud.Users.GetUserByName</c>, verification V30), so the server can search that owner's worlds. One request per
/// distinct name per session; a name that does not resolve is remembered too, and the term then stays a client-side
/// match. No own HTTP client (CLAUDE.md rule 7).
/// </summary>
internal sealed class AuthorResolver
{
    private readonly Engine _engine;
    private readonly ConcurrentDictionary<string, string?> _resolved = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _requested = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Names shorter than this are not looked up (each typed prefix would otherwise be its own request), and a session asks for at most <see cref="MaxLookups"/>.</summary>
    public const int MinNameLength = 3;

    public const int MaxLookups = 20;

    public AuthorResolver(Engine engine)
    {
        _engine = engine;
    }

    /// <summary>The user id for the name once it has resolved; null while pending and when the lookup found nobody.</summary>
    public string? IdFor(string name) => _resolved.TryGetValue(name, out var id) ? id : null;

    /// <summary>Starts the lookup if this name has not been asked for; <paramref name="done"/> runs when it finishes (any thread).</summary>
    public void Request(string name, Action done)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < MinNameLength || _requested.Count >= MaxLookups || !_requested.TryAdd(name, true))
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            string? id = null;
            try
            {
                var result = await _engine.Cloud.Users.GetUserByName(name);
                if (result.IsOK && result.Entity is { Id.Length: > 0 } user)
                {
                    id = user.Id;
                }
            }
            catch (Exception ex)
            {
                UniLog.Warning($"[Discover] Author lookup failed: {ex.GetType().Name}");
            }

            _resolved[name] = id;
            done();
        });
    }
}
