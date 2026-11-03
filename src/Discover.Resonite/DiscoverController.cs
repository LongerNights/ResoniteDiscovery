using Discover.Core.Lenses;
using Discover.Core.Model;
using Discover.Core.Presentation;
using Discover.Core.Settings;
using Discover.Resonite.Adapters;
using Discover.Resonite.Services;
using Discover.Resonite.Ui;
using Elements.Core;
using FrooxEngine;

namespace Discover.Resonite;

/// <summary>
/// The plain C# controller under a plain <see cref="RadiantDashScreen"/> (CLAUDE.md rule 6). It runs one tick per
/// userspace update, started with <c>World.RunInUpdates</c> (no component of ours is added to any world):
/// session events are drained (at most the configured number per tick), the screen is attached or re-attached,
/// and the card grid is refreshed only while the screen is shown. A tick that throws is caught at this boundary;
/// after 5 consecutive failures the controller pauses (spec "Error handling"). The stock Worlds tab is never touched.
/// </summary>
internal sealed class DiscoverController
{
    private const int MaxConsecutiveFailures = 5;
    private const double RefreshIntervalSeconds = 0.25;
    private const string ScreenLabel = "Galaxy Sieve";

    private readonly Engine _engine;
    private readonly World _world;
    private readonly DiscoverSettings _settings;
    private readonly DiscoveryIndex _index = new();
    private readonly SessionChangeQueue _queue = new();
    private readonly EngineSessionAdapter _adapter;
    private readonly JoinService _join;

    private RadiantDashScreen? _screen;
    private DiscoverUi? _ui;
    private LensKind _lens = LensKind.LiveNow;
    private volatile bool _dirty = true;
    private bool _loadedInitial;
    private bool _wasShown;
    private bool _paused;
    private bool _uiFailed;
    private bool _stopped;
    private int _failures;
    private int _frame;
    private DateTime _lastRefreshUtc = DateTime.MinValue;

    // Icon import runs off the update thread; the attach waits for it (the dash button reads the icon only once).
    private static readonly TimeSpan IconWait = TimeSpan.FromSeconds(10);
    private readonly DateTime _startedUtc = DateTime.UtcNow;
    private volatile Uri? _iconUri;
    private volatile bool _iconDone;

    // Lens transitions: the swap happens once the curtain covers the pane. If its callback never arrives (the tween
    // was cleared or the screen torn down), the swap is forced after TransitionTimeout.
    private static readonly TimeSpan TransitionTimeout = TimeSpan.FromSeconds(1);
    private bool _transitioning;
    private DateTime _transitionStartUtc;
    private bool _justShown;

    public DiscoverController(Engine engine, World userspace, DiscoverSettings settings)
    {
        _engine = engine;
        _world = userspace;
        _settings = settings;
        _join = new JoinService(engine);
        _adapter = new EngineSessionAdapter(engine) { HideIncompatible = !settings.ShowIncompatibleSessions };
        _adapter.Changed += _queue.Enqueue;
        engine.WorldManager.WorldsChanged += OnWorldsChanged;
        _ = LoadIconAsync();
    }

    private async Task LoadIconAsync()
    {
        _iconUri = await IconLoader.ImportAsync(_engine);
        _iconDone = true;
    }

    public void Start()
    {
        UniLog.Log("[Discover] Controller started");
        _world.RunInUpdates(1, Tick);
    }

    public void Stop()
    {
        _stopped = true;
        _engine.WorldManager.WorldsChanged -= OnWorldsChanged;
        _adapter.Dispose();
        UniLog.Log("[Discover] Controller stopped");
    }

    private void OnWorldsChanged() => _dirty = true; // the Open badge depends on which worlds are open

    private void Tick()
    {
        if (_stopped)
        {
            return;
        }

        try
        {
            if (!_paused)
            {
                Step();
            }

            _failures = 0;
        }
        catch (Exception ex)
        {
            _failures++;
            UniLog.Warning($"[Discover] Tick failed ({_failures}/{MaxConsecutiveFailures}): {ex.GetType().Name}: {ex.Message} at {FirstFrame(ex)}");
            if (_failures >= MaxConsecutiveFailures)
            {
                Pause();
            }
        }
        finally
        {
            if (!_stopped)
            {
                try
                {
                    _world.RunInUpdates(1, Tick);
                }
                catch (Exception)
                {
                    _stopped = true; // the world is gone
                }
            }
        }
    }

    private void Step()
    {
        if (!_loadedInitial)
        {
            _loadedInitial = true;
            _index.LoadSessions(_adapter.GetSessions());
            _dirty = true;
        }

        _adapter.Pump(_settings.SessionChangesPerTick);
        if (_queue.Drain(_index, _settings.SessionChangesPerTick).Count > 0)
        {
            _dirty = true;
        }

        // Cheap liveness check every frame; the heavier attach conditions only every 30 frames.
        if (_screen is null || _screen.IsRemoved)
        {
            _screen = null;
            _ui = null;
            if (_frame++ % 30 == 0)
            {
                TryAttach();
            }
        }

        if (_screen is null)
        {
            return;
        }

        var shown = _screen.IsShown;
        if (shown && !_wasShown)
        {
            _justShown = true; // refresh and fade in when the user opens the screen
        }

        _wasShown = shown;
        if (!shown)
        {
            return; // screen closed: no UI work (spec: under 0.1 ms per frame)
        }

        if (_ui is null && !_uiFailed)
        {
            _ui = BuildUi();
        }

        if (_ui is null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (_transitioning)
        {
            if (now - _transitionStartUtc < TransitionTimeout)
            {
                return; // the curtain is closing; the swap happens in its callback
            }

            UniLog.Warning("[Discover] Lens transition timed out; swapping without it");
            _transitioning = false;
            _ui.Settle();
            _dirty = true;
        }

        if (_justShown)
        {
            _justShown = false;
            _dirty = false;
            _lastRefreshUtc = now;
            Refresh(now);
            if (_settings.AnimateTransitions)
            {
                _ui.Reveal((float)_settings.TransitionIn.TotalSeconds, _settings.TransitionRise);
            }

            return;
        }

        if (_dirty && (now - _lastRefreshUtc).TotalSeconds >= RefreshIntervalSeconds)
        {
            _dirty = false;
            _lastRefreshUtc = now;
            Refresh(now);
        }
    }

    /// <summary>
    /// Attaches the screen once the dash exists and its saved workspace has finished loading: loading clears the
    /// screens container, which would destroy a screen attached earlier. If that happens anyway, the liveness check in
    /// <see cref="Step"/> notices and this runs again.
    /// </summary>
    private void TryAttach()
    {
        var dash = _world.GetGloballyRegisteredComponent<UserspaceRadiantDash>()?.Dash;
        var container = dash?.ScreensContainer;
        if (dash is null || container is null)
        {
            return;
        }

        var workspace = container.GetComponent<Workspace>();
        if (workspace is null || workspace.IsLoadingWorkspace || workspace.CurrentWorkspacePath is null || !dash.Screens.Any())
        {
            return;
        }

        // RadiantDashButton.Setup reads the icon once, so wait (briefly) for the galaxy icon before attaching.
        if (!_iconDone && DateTime.UtcNow - _startedUtc < IconWait)
        {
            return;
        }

        var screen = dash.AttachScreen(ScreenLabel, Theme.Tab, _iconUri ?? OfficialAssets.Graphics.Icons.Dash.Worlds);

        // The tab's label is a single line of auto-sized text; without <nobr> the space wraps "Galaxy Sieve" onto two
        // lines and it spills out of its box (the engine uses the same tag for its own "Old Home" label).
        screen.Label.Value = "<nobr>" + ScreenLabel + "</nobr>";

        // The dash screens are a saved cloud workspace; non-persistent slots are skipped when it saves
        // (verification V15, V22a). Without this the screen would be saved and duplicated on every launch.
        screen.Slot.PersistentSelf = false;

        // Sit right after the stock Worlds tab. Only our own slot is moved; the stock screen is only read.
        var worlds = dash.GetScreen<GridContainerScreen>(g => g.HasPreset(typeof(WorldsScreenInitializer)));
        screen.Slot.OrderOffset = worlds is null ? 5 : worlds.Slot.OrderOffset + 1;

        _screen = screen;
        _uiFailed = false;
        _wasShown = false;
        _dirty = true;
        UniLog.Log("[Discover] Screen attached");
    }

    private DiscoverUi? BuildUi()
    {
        var ui = new DiscoverUi(_screen!, _join, _settings.MaxCardSlots, SetLens, OnJoin);
        try
        {
            ui.Build();
            ui.HighlightLens(_lens);
            return ui;
        }
        catch (Exception ex)
        {
            // Build once only: remove the half-built UI and stop retrying, so a layout bug cannot pause the whole mod.
            _uiFailed = true;
            _screen!.ScreenCanvas.Slot.DestroyChildren();
            UniLog.Warning($"[Discover] UI build failed: {ex.GetType().Name}: {ex.Message} at {FirstFrame(ex)}");
            return null;
        }
    }

    private static string FirstFrame(Exception ex) =>
        ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim() ?? "unknown";

    private void SetLens(LensKind lens)
    {
        if (lens == _lens && !_transitioning)
        {
            return;
        }

        _lens = lens;
        if (_ui is null || !_settings.AnimateTransitions)
        {
            _ui?.HighlightLens(lens);
            _dirty = true;
            _lastRefreshUtc = DateTime.MinValue;
            return;
        }

        var ui = _ui;
        ui.HighlightLens(lens, (float)_settings.TransitionIn.TotalSeconds);
        _transitioning = true;
        _transitionStartUtc = DateTime.UtcNow;

        // A second click mid-transition restarts the cover; the newest lens is what the callback shows.
        ui.Cover((float)_settings.TransitionOut.TotalSeconds, () =>
        {
            if (!_transitioning || ui != _ui)
            {
                return;
            }

            _transitioning = false;
            try
            {
                // This runs inside the engine's tween update, outside the tick's try/catch.
                var now = DateTime.UtcNow;
                _lastRefreshUtc = now;
                _dirty = false;
                Refresh(now);
                ui.Reveal((float)_settings.TransitionIn.TotalSeconds, _settings.TransitionRise);
            }
            catch (Exception ex)
            {
                UniLog.Warning($"[Discover] Lens switch failed: {ex.GetType().Name}: {ex.Message} at {FirstFrame(ex)}");
                ui.Settle();
                _dirty = true;
            }
        });
    }

    private void Refresh(DateTime nowUtc)
    {
        var entries = _lens == LensKind.Friends ? LensEngine.Friends(_index.Entries) : LensEngine.LiveNow(_index.Entries);
        _ui!.ShowEntries(entries, nowUtc);

        var title = UiStrings.Get(_lens == LensKind.Friends ? "lens.friends" : "lens.liveNow");
        if (entries.Count == 0)
        {
            _ui.SetHeader(title, "", UiStrings.Get(_lens == LensKind.Friends ? "empty.friends" : "empty.liveNow"));
            return;
        }

        var people = _lens == LensKind.Friends ? entries.Sum(e => e.Sessions.Sum(s => s.ActiveUsers)) : entries.Sum(e => e.LiveUsers);
        var count = UiStrings.Format("status.summary", entries.Count, people);
        if (entries.Count > _ui.MaxCards)
        {
            count += " · " + UiStrings.Format("status.truncated", _ui.MaxCards);
        }

        _ui.SetHeader(title, count, null);
    }

    private void OnJoin(string key)
    {
        if (_index.TryGetEntry(key, out var entry))
        {
            _ = JoinAsync(entry);
        }
    }

    private async Task JoinAsync(DiscoveryEntry entry)
    {
        var outcome = await _join.JoinAsync(entry);
        JoinService.Report(outcome);
        _dirty = true;
    }

    private void Pause()
    {
        _paused = true;
        _adapter.Dispose();
        UniLog.Warning("[Discover] Paused after repeated errors");
        try
        {
            _ui?.SetHeader(UiStrings.Get("screen.title"), "", UiStrings.Get("status.paused"));
        }
        catch (Exception)
        {
            // The UI may be the thing that failed; the log line above is enough.
        }
    }
}
