using Discover.Core.Lenses;
using Discover.Core.Presentation;
using Discover.Core.Search;
using Discover.Core.Settings;
using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.UIX;
using Renderite.Shared;

namespace Discover.Resonite.Ui;

internal sealed partial class DiscoverUi
{
    // ---- join confirmation (the "extra menu" before joining, as on the stock Worlds tab)

    private const int MaxConfirmChoices = 4;
    private Slot _confirmScrim = null!;
    private Slot _confirmPanel = null!;
    private Text _confirmTitle = null!;
    private Text _confirmBody = null!;
    private readonly List<(Button Button, Text Label)> _confirmChoices = [];
    private readonly Action?[] _confirmActions = new Action?[MaxConfirmChoices];

    private void BuildConfirm(UIBuilder ui)
    {
        _confirmScrim = Catcher(ui, "ConfirmScrim", Theme.ConfirmScrim, CloseMenus);

        var slot = BeginFloating(ui, "Confirm", 0.26f, 0.74f, 0.5f, 0.5f, 10f, 22f);

        ui.Style.MinHeight = 58f;
        _confirmTitle = ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.MiddleCenter);
        Fix(_confirmTitle, TypeScale.Title);
        _confirmTitle.Color.Value = Theme.Heading;

        ui.Style.MinHeight = 84f;
        _confirmBody = ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.MiddleCenter);
        Fix(_confirmBody, TypeScale.Label);
        _confirmBody.Color.Value = Theme.Text;

        ui.Style.MinHeight = 54f;
        for (var i = 0; i < MaxConfirmChoices; i++)
        {
            var button = ui.Button((LocaleString)"", Theme.ButtonSelected);
            var label = button.Slot.GetComponentInChildren<Text>();
            Fix(label, TypeScale.Label);
            var index = i;
            button.LocalPressed += (_, _) =>
            {
                var action = _confirmActions[index];
                CloseMenus();
                action?.Invoke();
            };
            button.Slot.ActiveSelf = false;
            _confirmChoices.Add((button, label));
        }

        var cancel = ui.Button((LocaleString)UiStrings.Get("confirm.cancel"), Theme.ButtonQuiet);
        Fix(cancel.Slot.GetComponentInChildren<Text>(), TypeScale.Label);
        cancel.LocalPressed += (_, _) => CloseMenus();

        ui.NestOut();
        slot.ActiveSelf = false;
        _confirmPanel = slot;
    }

    /// <summary>Shows the confirmation panel: a title, a line of text, one button per choice (at most four) and Cancel.</summary>
    public void ShowConfirm(string title, string body, IReadOnlyList<(string Label, Action Action)> choices)
    {
        CloseMenus();
        _confirmTitle.Content.Value = title;
        _confirmBody.Content.Value = body;
        for (var i = 0; i < MaxConfirmChoices; i++)
        {
            var show = i < choices.Count;
            _confirmChoices[i].Button.Slot.ActiveSelf = show;
            _confirmChoices[i].Label.Content.Value = show ? choices[i].Label : "";
            _confirmActions[i] = show ? choices[i].Action : null;
        }

        _confirmScrim.ActiveSelf = true;
        _confirmPanel.ActiveSelf = true;
        AnimateOpen(_confirmPanel, ModalRise, _confirmScrim);
    }

    // ---- new session panel (modelled on the stock Session Settings dialog: who can join, then Start Session)

    private Slot _newSessionPanel = null!;
    private Text _newSessionWorld = null!;
    private Image _newSessionMobileTrack = null!;
    private RectTransform _newSessionMobileKnob = null!;
    private readonly List<(SkyFrost.Base.SessionAccessLevel? Level, Button Button, Image Dot)> _newSessionLevels = [];
    private SkyFrost.Base.SessionAccessLevel? _newSessionLevel;
    private bool _newSessionMobileFriendly;
    private Action<SkyFrost.Base.SessionAccessLevel?, bool>? _newSessionStart;

    private void BuildNewSession(UIBuilder ui)
    {
        var slot = BeginFloating(ui, "NewSession", 0.30f, 0.70f, 0.5f, 0.5f, 6f, 22f);

        ui.Style.MinHeight = 44f;
        var title = ui.Text((LocaleString)UiStrings.Get("newSession.title"), bestFit: true, alignment: Alignment.MiddleCenter);
        Fix(title, TypeScale.Title);
        title.Color.Value = Theme.Heading;

        ui.Style.MinHeight = 30f;
        _newSessionWorld = ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.MiddleCenter);
        Fix(_newSessionWorld, TypeScale.Body);
        _newSessionWorld.Color.Value = Theme.TextSecondary;

        NewSessionHeading(ui, "newSession.who");

        // Who can join: one row per level, a ring with a dot on the chosen one, and a hint after the name.
        ui.Style.MinHeight = 44f;
        (SkyFrost.Base.SessionAccessLevel? Level, string Name, string Hint)[] levels =
        [
            (null, UiStrings.Get("newSession.default"), UiStrings.Get("newSession.hint.default")),
            (SkyFrost.Base.SessionAccessLevel.Anyone, UiStrings.Get("stock.access.Anyone"), UiStrings.Get("newSession.hint.Anyone")),
            (SkyFrost.Base.SessionAccessLevel.RegisteredUsers, UiStrings.Get("stock.access.RegisteredUsers"), UiStrings.Get("newSession.hint.RegisteredUsers")),
            (SkyFrost.Base.SessionAccessLevel.ContactsPlus, UiStrings.Get("stock.access.ContactsPlus"), UiStrings.Get("newSession.hint.ContactsPlus")),
            (SkyFrost.Base.SessionAccessLevel.Contacts, UiStrings.Get("stock.access.Contacts"), UiStrings.Get("newSession.hint.Contacts")),
            (SkyFrost.Base.SessionAccessLevel.LAN, UiStrings.Get("stock.access.LAN"), UiStrings.Get("newSession.hint.LAN")),
            (SkyFrost.Base.SessionAccessLevel.Private, UiStrings.Get("stock.access.Private"), UiStrings.Get("newSession.hint.Private")),
        ];
        foreach (var (level, name, hint) in levels)
        {
            var button = ui.Button((LocaleString)$"<nobr>{name}   <size=75%><alpha=#99>{hint}</size></nobr>");
            var label = button.Slot.GetComponentInChildren<Text>();
            Fix(label, TypeScale.Body);
            label.HorizontalAlign.Value = TextHorizontalAlignment.Left;
            if (label.Slot.GetComponent<RectTransform>() is { } labelRect)
            {
                labelRect.OffsetMin.Value = new float2(52f, 0f);
                labelRect.OffsetMax.Value = new float2(-12f, 0f);
            }

            var inner = new UIBuilder(button.Slot.GetComponent<RectTransform>());
            RadiantUI_Constants.SetupDefaultStyle(inner);
            var ring = inner.Image(Theme.Rail);
            ring.Slot.Name = "Ring";
            ring.Sprite.Target = _rounded;
            ring.NineSliceSizing.Value = NineSliceSizing.FixedSize;
            ring.InteractionTarget.Value = false;
            ring.RectTransform.AnchorMin.Value = new float2(0f, 0.5f);
            ring.RectTransform.AnchorMax.Value = new float2(0f, 0.5f);
            ring.RectTransform.OffsetMin.Value = new float2(14f, -12f);
            ring.RectTransform.OffsetMax.Value = new float2(38f, 12f);
            var dotBuilder = new UIBuilder(ring.RectTransform);
            RadiantUI_Constants.SetupDefaultStyle(dotBuilder);
            var dot = dotBuilder.Image(Theme.Text);
            dot.Slot.Name = "Dot";
            dot.Sprite.Target = _rounded;
            dot.NineSliceSizing.Value = NineSliceSizing.FixedSize;
            dot.InteractionTarget.Value = false;
            dot.RectTransform.AnchorMin.Value = new float2(0.5f, 0.5f);
            dot.RectTransform.AnchorMax.Value = new float2(0.5f, 0.5f);
            dot.RectTransform.OffsetMin.Value = new float2(-6f, -6f);
            dot.RectTransform.OffsetMax.Value = new float2(6f, 6f);

            var captured = level;
            button.LocalPressed += (_, _) =>
            {
                _newSessionLevel = captured;
                RefreshNewSession();
            };
            _newSessionLevels.Add((level, button, dot));
        }

        NewSessionHeading(ui, "newSession.options");
        ui.Style.MinHeight = 48f;
        var mobile = SwitchButton(ui, UiStrings.Get("newSession.mobile"), out _, out _newSessionMobileTrack, out _newSessionMobileKnob);
        mobile.LocalPressed += (_, _) =>
        {
            _newSessionMobileFriendly = !_newSessionMobileFriendly;
            RefreshNewSession();
        };

        ui.Style.MinHeight = 56f;
        var row = ui.HorizontalLayout(10f, 0f, Alignment.MiddleCenter);
        var cancel = ui.Button((LocaleString)UiStrings.Get("confirm.cancel"), Theme.ButtonQuiet);
        Fix(cancel.Slot.GetComponentInChildren<Text>(), TypeScale.Body);
        cancel.LocalPressed += (_, _) => CloseMenus();
        var start = ui.Button((LocaleString)UiStrings.Get("newSession.start"), Theme.ButtonSelected);
        Fix(start.Slot.GetComponentInChildren<Text>(), TypeScale.Label);
        start.LocalPressed += (_, _) =>
        {
            var action = _newSessionStart;
            var level = _newSessionLevel;
            var mobileFriendly = _newSessionMobileFriendly;
            CloseMenus();
            action?.Invoke(level, mobileFriendly);
        };
        ui.NestOut();

        ui.NestOut();
        slot.ActiveSelf = false;
        _newSessionPanel = slot;
    }

    private static void NewSessionHeading(UIBuilder ui, string stringId)
    {
        ui.Style.MinHeight = 32f;
        var heading = ui.Text((LocaleString)UiStrings.Get(stringId), bestFit: true, alignment: Alignment.BottomLeft);
        Fix(heading, TypeScale.Caption);
        heading.Color.Value = Theme.TextSecondary;
    }

    private void RefreshNewSession()
    {
        foreach (var (level, button, dot) in _newSessionLevels)
        {
            var on = level == _newSessionLevel;
            SetColor(button, on ? Theme.ButtonSelected : Theme.Button);
            dot.Slot.ActiveSelf = on;
        }

        ApplySwitch(_newSessionMobileTrack, _newSessionMobileKnob, _newSessionMobileFriendly);
    }

    /// <summary>The new-session settings: who can join (default or one of six levels) and mobile friendly, then Start Session.</summary>
    public void ShowNewSession(string worldName, Action<SkyFrost.Base.SessionAccessLevel?, bool> start)
    {
        CloseMenus();
        _newSessionWorld.Content.Value = worldName;
        _newSessionLevel = null;
        _newSessionMobileFriendly = false;
        _newSessionStart = start;
        RefreshNewSession();
        _confirmScrim.ActiveSelf = true;
        _newSessionPanel.ActiveSelf = true;
        AnimateOpen(_newSessionPanel, ModalRise, _confirmScrim);
    }

    /// <summary>
    /// A popover that is exactly as tall as its content. The slot is anchored by a point on the vertical axis (its top,
    /// or its middle for <paramref name="pivotY"/> 0.5) and sized by a ContentSizeFitter, with the panel look, the
    /// vertical layout and the fitter all on the one slot. <paramref name="anchorMin"/> and <paramref name="anchorMax"/>
    /// give the horizontal range; the vertical anchor is <paramref name="anchorY"/>.
    /// </summary>
    private Slot BeginFloating(UIBuilder ui, string name, float xMin, float xMax, float anchorY, float pivotY, float spacing, float padding)
    {
        var slot = ui.Empty(name);
        var rect = slot.GetComponent<RectTransform>();
        rect.AnchorMin.Value = new float2(xMin, anchorY);
        rect.AnchorMax.Value = new float2(xMax, anchorY);
        rect.Pivot.Value = new float2(0.5f, pivotY);

        var image = slot.AttachComponent<Image>();
        image.Tint.Value = Theme.Rail;
        image.Sprite.Target = _rounded;
        image.NineSliceSizing.Value = NineSliceSizing.FixedSize;
        Absorb(image);

        var layout = slot.AttachComponent<VerticalLayout>();
        layout.Spacing.Value = spacing;
        layout.PaddingTop.Value = padding;
        layout.PaddingBottom.Value = padding;
        layout.PaddingLeft.Value = padding;
        layout.PaddingRight.Value = padding;
        layout.ChildAlignment = Alignment.TopCenter;
        layout.ForceExpandHeight.Value = false;

        var fitter = slot.AttachComponent<ContentSizeFitter>();
        fitter.VerticalFit.Value = SizeFit.PreferredSize;
        fitter.HorizontalFit.Value = SizeFit.Disabled;

        ui.NestInto(rect);
        return slot;
    }

    private Slot BuildMenu(UIBuilder ui, float2 anchorMin, float2 anchorMax, Action<UIBuilder> fill)
    {
        // anchorMax.y is the top edge; the height comes from the content. PositionMenu moves it under its button.
        var slot = BeginFloating(ui, "Menu", anchorMin.x, anchorMax.x, anchorMax.y, 1f, 6f, 14f);
        ui.Style.MinHeight = 46f;
        fill(ui);
        ui.NestOut();
        slot.ActiveSelf = false;
        return slot;
    }

    private void BuildPresetsMenu(UIBuilder ui)
    {
        var title = ui.Text((LocaleString)UiStrings.Get("presets.title"), bestFit: true, alignment: Alignment.MiddleLeft);
        Fix(title, TypeScale.Body);
        title.Color.Value = Theme.TextSecondary;

        ui.Style.MinHeight = 46f;
        var save = ui.Button((LocaleString)UiStrings.Get("presets.save"), Theme.ButtonSelected);
        save.LocalPressed += (_, _) => { CloseMenus(); _actions.SavePresetPrompt(); };

        ui.Style.MinHeight = 90f;
        _presetsEmpty = ui.Text((LocaleString)UiStrings.Get("presets.empty"), bestFit: true, alignment: Alignment.TopLeft);
        Fix(_presetsEmpty, TypeScale.Body);
        _presetsEmpty.Color.Value = Theme.TextSecondary;

        ui.Style.MinHeight = 42f;
        for (var i = 0; i < MaxPresetRows; i++)
        {
            var index = i;
            var row = ui.HorizontalLayout(6f, 0f, Alignment.MiddleCenter);
            ui.Style.FlexibleWidth = 1f;
            var load = ui.Button((LocaleString)"");
            var label = load.Slot.GetComponentInChildren<Text>();
            Fix(label, TypeScale.Body);
            load.LocalPressed += (_, _) => { CloseMenus(); _actions.ApplyPreset(index); };
            ui.Style.FlexibleWidth = -1f;
            ui.Style.MinWidth = 52f;
            var delete = ui.Button((LocaleString)"✕", Theme.ButtonQuiet);
            delete.LocalPressed += (_, _) => _actions.DeletePreset(index);
            ui.Style.MinWidth = -1f;
            ui.NestOut();
            row.Slot.ActiveSelf = false;
            _presetRows.Add((row.Slot, label));
        }
    }

    /// <summary>Puts text in the search box (a saved view being restored).</summary>
    public void SetSearchText(string text) => _search.TargetString = string.IsNullOrEmpty(text) ? null : text;

    /// <summary>Lists the saved views in the Presets menu. Cheap when nothing changed.</summary>
    public void SetPresets(IReadOnlyList<string> names)
    {
        var key = string.Join("\n", names);
        if (key == _presetsShown)
        {
            return;
        }

        _presetsShown = key;
        _presetsEmpty.Slot.ActiveSelf = names.Count == 0;
        for (var i = 0; i < _presetRows.Count; i++)
        {
            var show = i < names.Count;
            _presetRows[i].Row.ActiveSelf = show;
            _presetRows[i].Label.Content.Value = show ? "<nobr>" + RichText.Neutralize(names[i]) + "</nobr>" : "";
        }
    }

    private void BuildSortMenu(UIBuilder ui)
    {
        var title = ui.Text((LocaleString)UiStrings.Get("sort.title"), bestFit: true, alignment: Alignment.MiddleLeft);
        Fix(title, TypeScale.Body);
        title.Color.Value = Theme.TextSecondary;

        // The direction is a two-way control at the top (hidden for Random, which has none).
        ui.Style.MinHeight = 46f;
        var row = ui.HorizontalLayout(6f, 0f, Alignment.MiddleCenter);
        _sortDirectionRow = row.Slot;
        _descending = ui.Button((LocaleString)UiStrings.Get("sort.descending"));
        Fix(_descending.Slot.GetComponentInChildren<Text>(), TypeScale.Body);
        _descending.LocalPressed += (_, _) => _actions.SetDescending(true);
        _ascending = ui.Button((LocaleString)UiStrings.Get("sort.ascending"));
        Fix(_ascending.Slot.GetComponentInChildren<Text>(), TypeScale.Body);
        _ascending.LocalPressed += (_, _) => _actions.SetDescending(false);
        ui.NestOut();

        ui.Style.MinHeight = 42f;
        foreach (var key in Sorting.All)
        {
            var button = ui.Button((LocaleString)"");
            var rowLabel = button.Slot.GetComponentInChildren<Text>();
            Fix(rowLabel, TypeScale.Body);
            rowLabel.HorizontalAlign.Value = TextHorizontalAlignment.Left;
            if (rowLabel.Slot.GetComponent<RectTransform>() is { } rowRect)
            {
                rowRect.OffsetMin.Value = new float2(16f, 0f);
                rowRect.OffsetMax.Value = new float2(-150f, 0f);
            }

            // The hint ("Total visits") sits at the right edge, smaller and dimmer.
            var inner = new UIBuilder(button.Slot.GetComponent<RectTransform>());
            RadiantUI_Constants.SetupDefaultStyle(inner);
            var hint = inner.Text((LocaleString)"", bestFit: true, alignment: Alignment.MiddleRight);
            Fix(hint, TypeScale.Caption);
            hint.Color.Value = Theme.TextSecondary;
            if (hint.Slot.GetComponent<RectTransform>() is { } hintRect)
            {
                hintRect.OffsetMin.Value = new float2(0f, 0f);
                hintRect.OffsetMax.Value = new float2(-16f, 0f);
            }

            var captured = key;
            button.LocalPressed += (_, _) => { _actions.ChooseSort(captured); CloseMenus(); };
            _sortItems[key] = (button, rowLabel);
            _sortHints[key] = hint;
        }
    }

    private Slot _sortDirectionRow = null!;

    // Filter rows are grouped by the kind of tab they apply to; RenderFiltersMenu shows only the group that fits.
    private readonly List<Slot> _filtersForSessions = [];
    private readonly List<Slot> _filtersForSearch = [];
    private Text _filtersNone = null!;
    private readonly Dictionary<FilterOp, (Image Track, RectTransform Knob)> _switches = [];

    private void BuildFiltersMenu(UIBuilder ui)
    {
        var title = ui.Text((LocaleString)UiStrings.Get("filters.title"), bestFit: true, alignment: Alignment.MiddleLeft);
        Fix(title, TypeScale.Title);
        title.Color.Value = Theme.Heading;

        ui.Style.MinHeight = 60f;
        _filtersNone = ui.Text((LocaleString)UiStrings.Get("filters.none"), bestFit: true, alignment: Alignment.TopLeft);
        Fix(_filtersNone, TypeScale.Body);
        _filtersNone.Color.Value = Theme.TextSecondary;
        ui.Style.MinHeight = 46f;

        // Session tabs (Live Now, Friends)
        FilterHeading(ui, "filters.section.people", _filtersForSessions);
        _filtersForSessions.Add(Stepper(ui, FilterOp.MinUsersDown, FilterOp.MinUsersUp));
        _filtersForSessions.Add(SwitchRow(ui, FilterOp.ToggleHasRoom, "filters.hasRoom"));
        _filtersForSessions.Add(SwitchRow(ui, FilterOp.ToggleVr, "filters.vr"));
        _filtersForSessions.Add(SwitchRow(ui, FilterOp.ToggleMobile, "filters.mobile"));
        FilterHeading(ui, "filters.section.session", _filtersForSessions);
        _filtersForSessions.Add(Stepper(ui, FilterOp.MinUptimeDown, FilterOp.MinUptimeUp));

        var host = ui.HorizontalLayout(6f, 0f, Alignment.MiddleCenter);
        _filtersForSessions.Add(host.Slot);
        foreach (var (op, id) in new[] { (FilterOp.HostAny, "filters.host.any"), (FilterOp.HostPerson, "filters.host.person"), (FilterOp.HostHeadless, "filters.host.headless") })
        {
            var button = ui.Button((LocaleString)UiStrings.Get(id));
            Fix(button.Slot.GetComponentInChildren<Text>(), TypeScale.Body);
            var captured = op;
            button.LocalPressed += (_, _) => _actions.Filter(captured);
            _filterItems[op] = (button, button.Slot.GetComponentInChildren<Text>());
        }

        ui.NestOut();
        _filtersForSessions.Add(SwitchRow(ui, FilterOp.ToggleIncludeEmpty, "filters.includeEmpty"));
        _filtersForSessions.Add(SwitchRow(ui, FilterOp.ToggleOtherVersions, "filters.otherVersions"));

        // Search tabs (Browse, Featured, New, Random, My Worlds, Group Worlds)
        FilterHeading(ui, "filters.section.world", _filtersForSearch);
        _filtersForSearch.Add(Stepper(ui, FilterOp.MinVisitsDown, FilterOp.MinVisitsUp));
        _filtersForSearch.Add(SwitchRow(ui, FilterOp.ToggleFeatured, "filters.featured"));

        ui.Style.MinHeight = 50f;
        var footer = ui.HorizontalLayout(8f, 0f, Alignment.MiddleCenter);
        var reset = ui.Button((LocaleString)UiStrings.Get("filters.reset"), Theme.ButtonQuiet);
        Fix(reset.Slot.GetComponentInChildren<Text>(), TypeScale.Body);
        reset.LocalPressed += (_, _) => _actions.Filter(FilterOp.Reset);
        var done = ui.Button((LocaleString)UiStrings.Get("filters.done"), Theme.ButtonSelected);
        Fix(done.Slot.GetComponentInChildren<Text>(), TypeScale.Body);
        done.LocalPressed += (_, _) => CloseMenus();
        ui.NestOut();
    }

    private void FilterHeading(UIBuilder ui, string stringId, List<Slot> group)
    {
        ui.Style.MinHeight = 32f;
        var heading = ui.Text((LocaleString)UiStrings.Get(stringId), bestFit: true, alignment: Alignment.BottomLeft);
        Fix(heading, TypeScale.Caption);
        heading.Color.Value = Theme.TextSecondary;
        group.Add(heading.Slot);
        ui.Style.MinHeight = 46f;
    }

    /// <summary>A "label ... switch" row: the whole row is the button, and the track shows on or off.</summary>
    private Slot SwitchRow(UIBuilder ui, FilterOp op, string stringId)
    {
        var button = SwitchButton(ui, UiStrings.Get(stringId), out var label, out var track, out var knob);
        var captured = op;
        button.LocalPressed += (_, _) => _actions.Filter(captured);
        _filterItems[op] = (button, label);
        _switches[op] = (track, knob);
        return button.Slot;
    }

    /// <summary>The row button of a switch: label at the left, a track and knob at the right. See <see cref="ApplySwitch"/>.</summary>
    private Button SwitchButton(UIBuilder ui, string text, out Text label, out Image track, out RectTransform knob)
    {
        var button = ui.Button((LocaleString)text);
        label = button.Slot.GetComponentInChildren<Text>();
        Fix(label, TypeScale.Body);
        label.HorizontalAlign.Value = TextHorizontalAlignment.Left;
        if (label.Slot.GetComponent<RectTransform>() is { } labelRect)
        {
            labelRect.OffsetMin.Value = new float2(16f, 0f);
            labelRect.OffsetMax.Value = new float2(-80f, 0f);
        }

        var inner = new UIBuilder(button.Slot.GetComponent<RectTransform>());
        RadiantUI_Constants.SetupDefaultStyle(inner);
        track = inner.Image(Theme.Rail);
        track.Slot.Name = "Track";
        track.Sprite.Target = _rounded;
        track.NineSliceSizing.Value = NineSliceSizing.FixedSize;
        track.InteractionTarget.Value = false;
        track.RectTransform.AnchorMin.Value = new float2(1f, 0.5f);
        track.RectTransform.AnchorMax.Value = new float2(1f, 0.5f);
        track.RectTransform.OffsetMin.Value = new float2(-66f, -14f);
        track.RectTransform.OffsetMax.Value = new float2(-14f, 14f);

        var knobBuilder = new UIBuilder(track.RectTransform);
        RadiantUI_Constants.SetupDefaultStyle(knobBuilder);
        var knobImage = knobBuilder.Image(Theme.Text);
        knobImage.Slot.Name = "Knob";
        knobImage.Sprite.Target = _rounded;
        knobImage.NineSliceSizing.Value = NineSliceSizing.FixedSize;
        knobImage.InteractionTarget.Value = false;
        knob = knobImage.RectTransform;
        knob.AnchorMin.Value = new float2(KnobOff, 0f);
        knob.AnchorMax.Value = new float2(KnobOff, 1f);
        knob.OffsetMin.Value = new float2(-11f, 3f);
        knob.OffsetMax.Value = new float2(11f, -3f);
        return button;
    }

    /// <summary>Slides a switch's knob to the on or off end and colours its track.</summary>
    // The knob is a point-anchored square: its anchor x is where its centre sits on the 52 px track (14 px and 38 px in).
    private const float KnobOff = 0.269f;
    private const float KnobOn = 0.731f;

    /// <summary>Moves a switch's knob to the on or off end and colours its track; slides when animations are on and it is on screen.</summary>
    private static void ApplySwitch(Image track, RectTransform knob, bool on)
    {
        var x = on ? KnobOn : KnobOff;
        var tint = on ? Theme.ButtonSelected : Theme.Rail;
        knob.OffsetMin.Value = new float2(-11f, 3f);
        knob.OffsetMax.Value = new float2(11f, -3f);
        var min = new float2(x, 0f);
        var max = new float2(x, 1f);
        if (!UiMotion.Enabled || !knob.Slot.IsActive || Math.Abs(knob.AnchorMin.Value.x - x) < 0.001f)
        {
            knob.AnchorMin.Value = min;
            knob.AnchorMax.Value = max;
            track.Tint.Value = tint;
            return;
        }

        try
        {
            var seconds = UiMotion.Seconds(0.16f);
            knob.AnchorMin.TweenTo(min, seconds, CurvePreset.CubicOut);
            knob.AnchorMax.TweenTo(max, seconds, CurvePreset.CubicOut);
            track.Tint.TweenTo(tint, seconds, CurvePreset.SineOut);
        }
        catch (Exception ex)
        {
            UniLog.Warning($"[Discover] Switch animation failed: {ex.GetType().Name}");
            knob.AnchorMin.Value = min;
            knob.AnchorMax.Value = max;
            track.Tint.Value = tint;
        }
    }

    /// <summary>A "- value +" row: the middle button shows the value and does nothing.</summary>
    private Slot Stepper(UIBuilder ui, FilterOp down, FilterOp up)
    {
        var row = ui.HorizontalLayout(8f, 0f, Alignment.MiddleCenter);
        ui.Style.MinWidth = 56f;
        var minus = ui.Button((LocaleString)"−");
        Fix(minus.Slot.GetComponentInChildren<Text>(), TypeScale.Label);
        minus.LocalPressed += (_, _) => _actions.Filter(down);
        ui.Style.MinWidth = 0f;
        ui.Style.FlexibleWidth = 1f;
        var label = ui.Button((LocaleString)"", Theme.ButtonQuiet);
        Fix(label.Slot.GetComponentInChildren<Text>(), TypeScale.Body);
        ui.Style.FlexibleWidth = -1f;
        ui.Style.MinWidth = 56f;
        var plus = ui.Button((LocaleString)"+");
        Fix(plus.Slot.GetComponentInChildren<Text>(), TypeScale.Label);
        plus.LocalPressed += (_, _) => _actions.Filter(up);
        ui.Style.MinWidth = 0f;
        ui.NestOut();
        _filterItems[up] = (label, label.Slot.GetComponentInChildren<Text>());
        return row.Slot;
    }

    private Button _sortBtn = null!;
    private Button _filtersBtn = null!;
    private Button _presetsBtn = null!;

    // The top-bar button that opens each popover; the popover is placed under it when it opens (PositionMenu).
    private readonly Dictionary<Slot, Button> _menuAnchors = [];

    /// <summary>
    /// Centres a popover under the button that opens it, keeping its width and staying inside the pane, so moving or
    /// resizing the top bar's buttons never leaves a popover misplaced. The sizes come from the layout's computed rects
    /// (global space, so the ratios do not depend on the canvas scale). If anything is degenerate the popover keeps the
    /// position it was built with.
    /// </summary>
    private void PositionMenu(Slot menu)
    {
        try
        {
            if (!_menuAnchors.TryGetValue(menu, out var anchor) || menu.Parent?.GetComponent<RectTransform>() is not { } host
                || anchor.Slot.GetComponent<RectTransform>() is not { } button)
            {
                return;
            }

            var menuRect = menu.GetComponent<RectTransform>();
            var hostRect = host.ComputeGlobalComputeRect();
            var buttonRect = button.ComputeGlobalComputeRect();
            if (hostRect.size.x < 1f || hostRect.size.y < 1f || buttonRect.size.x < 1f)
            {
                return;
            }

            var width = menuRect.AnchorMax.Value.x - menuRect.AnchorMin.Value.x;
            var centre = (buttonRect.position.x + buttonRect.size.x * 0.5f - hostRect.position.x) / hostRect.size.x;
            var left = MathF.Max(0.01f, MathF.Min(centre - width * 0.5f, 0.99f - width));
            var top = (buttonRect.position.y - hostRect.position.y) / hostRect.size.y - 0.01f;
            var min = menuRect.AnchorMin.Value;
            var max = menuRect.AnchorMax.Value;
            var topY = top is > 0.4f and < 1f ? top : max.y;
            menuRect.AnchorMin.Value = new float2(left, topY);
            menuRect.AnchorMax.Value = new float2(left + width, topY);
        }
        catch (Exception ex)
        {
            UniLog.Warning($"[Discover] Placing a popover failed: {ex.GetType().Name}");
        }
    }

    /// <summary>Panel open animations (the Animations setting). Off: panels just appear.</summary>
    public bool Animate
    {
        get => UiMotion.Enabled;
        set => UiMotion.Enabled = value;
    }

    internal readonly PanTuning PanTuning = new();

    /// <summary>The preview pan speed setting, applied to every card's and the detail panel's preview at once.</summary>
    public float PreviewPanSpeed
    {
        get => PanTuning.Multiplier;
        set => PanTuning.Set(value);
    }

    /// <summary>1 is normal; see <see cref="UiMotion.Speed"/>.</summary>
    public float AnimationSpeed
    {
        get => UiMotion.Speed;
        set => UiMotion.Speed = value;
    }

    /// <summary>
    /// Slides a panel into place by tweening its anchors from a small offset (<paramref name="from"/>, in fractions of the
    /// pane) to where they are. Only the anchors move: the panel's size comes from its layout, and its tint is driven by
    /// its press-absorbing button, so neither can be tweened.
    /// </summary>
    /// <summary>Fades the dimming behind a modal in. The scrim's tint is driven by its button, so the driver's colours are tweened.</summary>
    private static void FadeScrim(Slot scrim)
    {
        if (!UiMotion.Enabled || scrim.GetComponent<Button>() is not { } button)
        {
            return;
        }

        var driver = button.ColorDrivers[0];
        var target = Theme.ConfirmScrim;
        var fade = UiMotion.Seconds(0.24f);
        driver.NormalColor.TweenFromTo(target.SetA(0f), target, fade, CurvePreset.SineOut);
        driver.HighlightColor.TweenFromTo(target.SetA(0f), target, fade, CurvePreset.SineOut);
    }

    private void AnimateOpen(Slot panel, float2 from, Slot? scrim = null, CurvePreset curve = CurvePreset.QuinticOut)
    {
        if (!Animate || panel.GetComponent<RectTransform>() is not { } rect)
        {
            return;
        }

        try
        {
            var min = rect.AnchorMin.Value;
            var max = rect.AnchorMax.Value;
            var seconds = UiMotion.Seconds(curve == CurvePreset.QuinticOut ? 0.30f : 0.26f);
            rect.AnchorMin.TweenFromTo(min + from, min, seconds, curve);
            rect.AnchorMax.TweenFromTo(max + from, max, seconds, curve);

            // The dimming behind a modal fades in with it. The scrim's tint is driven by its button, so the driver's
            // colours are what is tweened.
            if (scrim?.GetComponent<Button>() is { } button)
            {
                var driver = button.ColorDrivers[0];
                var target = Theme.ConfirmScrim;
                var fade = UiMotion.Seconds(0.24f);
                driver.NormalColor.TweenFromTo(target.SetA(0f), target, fade, CurvePreset.SineOut);
                driver.HighlightColor.TweenFromTo(target.SetA(0f), target, fade, CurvePreset.SineOut);
            }
        }
        catch (Exception ex)
        {
            UniLog.Warning($"[Discover] Panel animation failed: {ex.GetType().Name}");
        }
    }

    private static readonly float2 ModalRise = new(0f, -0.04f);
    private static readonly float2 MenuDrop = new(0f, 0.03f);

    private void ToggleMenu(Slot menu)
    {
        var open = !menu.ActiveSelf;
        CloseMenus();
        if (open)
        {
            PositionMenu(menu);
        }

        menu.ActiveSelf = open;
        _scrim.ActiveSelf = open;
        if (open)
        {
            AnimateOpen(menu, MenuDrop, null, CurvePreset.ReboundOutSmall);
        }
    }

    public void CloseMenus()
    {
        _sortMenu.ActiveSelf = false;
        _filtersMenu.ActiveSelf = false;
        _presetsMenu.ActiveSelf = false;
        _suggestMenu.ActiveSelf = false;
        _scrim.ActiveSelf = false;
        _confirmPanel.ActiveSelf = false;
        _newSessionPanel.ActiveSelf = false;
        _confirmScrim.ActiveSelf = false;
        _organizePanel.ActiveSelf = false;
        _organizeScrim.ActiveSelf = false;
        _settingsPanel.ActiveSelf = false;
        _startLensMenu.ActiveSelf = false;
        _settingsScrim.ActiveSelf = false;
        _detailPanel.ActiveSelf = false;
        _detailSheet.ActiveSelf = false;
        _detailScrim.ActiveSelf = false;
        ClosePrompt();
    }
}
