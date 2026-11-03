using Discover.Core.Settings;
using Elements.Core;
using FrooxEngine;

namespace Discover.Resonite;

/// <summary>
/// Loader-agnostic entry point (spec Integration §6). Each loader shim calls <see cref="Start"/> and
/// <see cref="Stop"/> and nothing else. Safe to call from any thread.
/// </summary>
public static class DiscoverMod
{
    private static readonly object Gate = new();
    private static Engine? _engine;
    private static DiscoverController? _controller;

    public static bool IsRunning => _engine is not null;

    public static void Start(Engine engine)
    {
        lock (Gate)
        {
            if (_engine is not null)
            {
                return;
            }

            _engine = engine;
        }

        UniLog.Log("[Discover] Started");
        engine.WorldManager.WorldAdded += OnWorldAdded;

        var userspace = Userspace.UserspaceWorld;
        if (userspace is not null)
        {
            Attach(userspace);
        }
    }

    public static void Stop()
    {
        DiscoverController? controller;
        Engine? engine;
        lock (Gate)
        {
            engine = _engine;
            controller = _controller;
            _engine = null;
            _controller = null;
        }

        if (engine is null)
        {
            return;
        }

        engine.WorldManager.WorldAdded -= OnWorldAdded;
        controller?.Stop();
        UniLog.Log("[Discover] Stopped");
    }

    /// <summary>
    /// <c>IsUserspace()</c> is <c>world == Userspace.UserspaceWorld</c>, and that static is not assigned yet when
    /// <c>WorldAdded</c> fires for the userspace world (seen in game 2026-11-02: the check was false and the controller
    /// never started). So each new world is re-checked on its own next updates instead of at event time.
    /// </summary>
    private static void OnWorldAdded(World world) => Probe(world, 0);

    private const int MaxProbeUpdates = 120;

    private static void Probe(World world, int attempt)
    {
        if (_controller is not null)
        {
            return;
        }

        try
        {
            world.RunInUpdates(1, () =>
            {
                if (world.IsUserspace())
                {
                    Attach(world);
                }
                else if (Userspace.UserspaceWorld is null && attempt < MaxProbeUpdates)
                {
                    Probe(world, attempt + 1); // userspace not assigned yet: look again next update
                }
            });
        }
        catch (Exception ex)
        {
            UniLog.Warning($"[Discover] Could not probe a new world: {ex.GetType().Name}");
        }
    }

    private static void Attach(World userspace)
    {
        // WorldAdded may arrive on any thread; the controller must start on the world's update thread.
        userspace.RunSynchronously(() =>
        {
            Engine engine;
            lock (Gate)
            {
                if (_engine is null || _controller is not null)
                {
                    return;
                }

                engine = _engine;
                _controller = new DiscoverController(engine, userspace, new DiscoverSettings());
            }

            _controller.Start();
        });
    }
}
