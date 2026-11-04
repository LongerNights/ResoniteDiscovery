using Discover.Core.Lenses;
using Discover.Core.Presentation;
using Discover.Core.Search;
using Discover.Core.Settings;
using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.UIX;

namespace Discover.Resonite.Ui;

/// <summary>
/// The Galaxy Sieve screen (spec "UI", design in docs/m3-design.md): a lens rail; a top bar with search, sort, filters
/// and refresh; a chip bar; a header; the card grid; a status bar with page buttons; two popovers (sort, filters); and a
/// transition curtain. Built once, the first time the screen is shown, then only re-bound by <see cref="Render"/>.
///
/// UIBuilder rules that matter here (verification.md): <c>Panel</c>, layouts and <c>Button</c> nest by themselves;
/// split gaps are fractions; <c>HorizontalHeader</c> is a top strip. Text comes from <see cref="UiStrings"/>,
/// colours from <see cref="Theme"/>. Not more than <see cref="MaxCards"/> card slots exist.
/// </summary>
internal sealed class DiscoverUi
{
    internal const int MaxChips = 12;
    private static readonly float2 CellSize = new(360f, 300f);
    private static readonly float2 Spacing = new(16f, 16f);
    private static readonly float2 ListCellSize = new(960f, 124f);
    private static readonly float2 ListSpacing = new(16f, 10f);

    private readonly RadiantDashScreen _screen;
    private readonly IBrowseActions _actions;

    private const int MaxListButtons = 5;
    private readonly Dictionary<LensKind, (Button Button, Text Label)> _rail = [];
    private readonly List<(Button Button, Text Label)> _listButtons = [];
    private readonly string?[] _listIds = new string?[MaxListButtons];
    private Slot _listTools = null!;
    private readonly List<CardView> _cards = [];
    private readonly List<(Button Button, Text Label)> _chips = [];
    private readonly Dictionary<SortKey, (Button Button, Text Label)> _sortItems = [];
    private readonly Dictionary<FilterOp, (Button Button, Text Label)> _filterItems = [];

    private Slot _content = null!;
    private Text _title = null!;
    private Text _subtitle = null!;
    private Text _count = null!;
    private Text _status = null!;
    private Text _emptyText = null!;
    private Slot _emptyArea = null!;
    private Slot _clearFiltersButton = null!;
    private Text _sortButtonLabel = null!;
    private Text _filtersButtonLabel = null!;
    private Button _previous = null!;
    private Button _next = null!;
    private Button _clearChips = null!;
    private Slot _banner = null!;
    private Text _bannerText = null!;
    private RectTransform _bannerRect = null!;
    private RectTransform _bodyRect = null!;
    private Slot _scrim = null!;
    private Slot _sortMenu = null!;
    private Slot _filtersMenu = null!;
    private Slot _presetsMenu = null!;
    private const int MaxPresetRows = FilterPresets.MaxPresets;
    private readonly List<(Slot Row, Text Label)> _presetRows = [];
    private Text _presetsEmpty = null!;
    private string _presetsShown = "\0";
    private Button _descending = null!;
    private Button _ascending = null!;
    private TextField _search = null!;
    private Image _curtain = null!;
    private RectTransform _viewport = null!;
    private float2 _viewportMin;
    private float2 _viewportMax;
    private int _curtainGeneration;
    private SpriteProvider _rounded = null!;

    public DiscoverUi(RadiantDashScreen screen, int maxCards, IBrowseActions actions)
    {
        _screen = screen;
        MaxCards = maxCards;
        _actions = actions;
    }

    public int MaxCards { get; }

    // ------------------------------------------------------------------ building

    public void Build()
    {
        _rounded = RadiantUI_Constants.GetButtonSprite(_screen.World);
        var ui = new UIBuilder(_screen.ScreenCanvas);
        RadiantUI_Constants.SetupDefaultStyle(ui);
        ui.Style.ButtonColor = Theme.Button;
        ui.Panel(Theme.Background); // Panel nests into the new element
        ui.SplitHorizontally(0.16f, out var rail, out var main);
        BuildRail(ui, rail);
        BuildMain(ui, main);
    }

    private void BuildRail(UIBuilder ui, RectTransform rail)
    {
        ui.NestInto(rail);
        ui.Panel(Theme.Rail);
        var layout = ui.VerticalLayout(8f, 14f, Alignment.TopCenter); // Panel and VerticalLayout each nest
        layout.ForceExpandHeight.Value = false;

        ui.Style.MinHeight = 72f;
        var title = ui.Text((LocaleString)UiStrings.Get("screen.title"), bestFit: true, alignment: Alignment.MiddleCenter);
        Fix(title, TypeScale.Hero);
        title.Color.Value = Theme.Heading;

        foreach (var (label, lenses) in new[]
        {
            (UiStrings.Get("rail.now"), new[] { LensKind.CurrentWorlds, LensKind.LiveNow, LensKind.Friends }),
            (UiStrings.Get("rail.discover"), new[] { LensKind.Browse, LensKind.Featured, LensKind.New, LensKind.Random }),
        })
        {
            ui.Style.MinHeight = 34f;
            var section = ui.Text((LocaleString)label, bestFit: true, alignment: Alignment.BottomLeft);
            _railHeaders[label] = section.Slot;
            Fix(section, TypeScale.Body);
            section.Color.Value = Theme.TextSecondary;

            ui.Style.MinHeight = 40f;
            foreach (var lens in lenses)
            {
                AddRailButton(ui, lens);
            }
        }

        // MINE: Favorites, the named lists (a small pool), "+ New list", Recent.
        ui.Style.MinHeight = 34f;
        var mine = ui.Text((LocaleString)UiStrings.Get("rail.yours"), bestFit: true, alignment: Alignment.BottomLeft);
        _railHeaders[UiStrings.Get("rail.yours")] = mine.Slot;
        Fix(mine, TypeScale.Body);
        mine.Color.Value = Theme.TextSecondary;

        ui.Style.MinHeight = 40f;
        AddRailButton(ui, LensKind.Favorites);
        for (var i = 0; i < MaxListButtons; i++)
        {
            var button = ui.Button((LocaleString)"");
            var label = button.Slot.GetComponentInChildren<Text>();
            var index = i;
            button.LocalPressed += (_, _) =>
            {
                if (_listIds[index] is { } id)
                {
                    _actions.SelectList(id);
                }
            };
            _listExtras.Add(DressRailRow(button, label));
            button.Slot.ActiveSelf = false;
            _listButtons.Add((button, label));
        }

        _newListButton = ui.Button((LocaleString)UiStrings.Get("rail.newList"), Theme.ButtonQuiet);
        _newListButton.LocalPressed += (_, _) => _actions.NewListPrompt();
        AddRailButton(ui, LensKind.Recent);
        AddRailButton(ui, LensKind.MyWorlds);
        AddRailButton(ui, LensKind.GroupWorlds);

        ui.Style.MinHeight = 34f;
        var insights = ui.Text((LocaleString)UiStrings.Get("rail.insights"), bestFit: true, alignment: Alignment.BottomLeft);
        _railHeaders[UiStrings.Get("rail.insights")] = insights.Slot;
        Fix(insights, TypeScale.Body);
        insights.Color.Value = Theme.TextSecondary;
        ui.Style.MinHeight = 40f;
        foreach (var lens in new[] { LensKind.Trending, LensKind.Rising, LensKind.HiddenGems })
        {
            AddRailButton(ui, lens);
        }

        ui.NestOut();
        ui.NestOut();
        ui.NestOut();
    }

    private readonly Dictionary<string, Slot> _railHeaders = [];
    private readonly List<(Text Count, Slot Bar)> _listExtras = [];
    private Button _newListButton = null!;

    private readonly Dictionary<LensKind, (Text Count, Slot Bar)> _railExtras = [];

    /// <summary>
    /// Makes a rail button a list row: the label left-aligned, a count at the right edge, and a thin accent bar on the
    /// left that shows when the row is the selected one.
    /// </summary>
    private static (Text Count, Slot Bar) DressRailRow(Button button, Text label)
    {
        label.HorizontalAlign.Value = TextHorizontalAlignment.Left;
        label.Slot.GetComponent<RectTransform>()?.AddFixedPadding(12f);

        var rect = button.Slot.GetComponent<RectTransform>();
        var inner = new UIBuilder(rect);
        RadiantUI_Constants.SetupDefaultStyle(inner);
        var count = inner.Text((LocaleString)"", bestFit: true, alignment: Alignment.MiddleRight);
        Fix(count, TypeScale.Caption);
        count.Color.Value = Theme.TextSecondary;
        count.Slot.GetComponent<RectTransform>()?.AddFixedPadding(12f);
        count.Slot.Name = "Count";

        var bar = inner.Image(Theme.Heading);
        bar.Slot.Name = "AccentBar";
        bar.InteractionTarget.Value = false;
        bar.RectTransform.AnchorMax.Value = new float2(0f, 1f);
        bar.RectTransform.OffsetMax.Value = new float2(5f, 0f);
        bar.Slot.ActiveSelf = false;
        return (count, bar.Slot);
    }

    private void AddRailButton(UIBuilder ui, LensKind lens)
    {
        var button = ui.Button((LocaleString)RailBuilder.Title(lens));
        var text = button.Slot.GetComponentInChildren<Text>();
        button.LocalPressed += (_, _) => _actions.SelectLens(lens);
        _rail[lens] = (button, text);
        _railExtras[lens] = DressRailRow(button, text);
    }

    private void BuildMain(UIBuilder ui, RectTransform main)
    {
        ui.NestInto(main);

        // Top strip regions, top to bottom: top bar, chip bar, header, then the body and a footer.
        ui.HorizontalHeader(92f, out var topBar, out var below1);
        ui.NestInto(below1);
        ui.HorizontalHeader(56f, out var chipBar, out var below2);
        ui.NestInto(below2);
        ui.HorizontalHeader(84f, out var header, out var below3);
        ui.NestInto(below3);
        ui.HorizontalFooter(68f, out var footer, out var body);

        BuildTopBar(ui, topBar);
        BuildChipBar(ui, chipBar);
        BuildHeader(ui, header);
        BuildFooter(ui, footer);
        BuildBody(ui, body);

        ui.NestOut(); // below3
        ui.NestOut(); // below2
        ui.NestOut(); // below1
        BuildPopovers(ui, main);

        // Transition curtain: the last child of the main pane, so it draws over everything in it. It never takes
        // pointer hits and is inactive whenever it is fully transparent.
        _curtain = ui.Image(Theme.CurtainHidden);
        _curtain.InteractionTarget.Value = false;
        _curtain.Slot.ActiveSelf = false;
        ui.NestOut();
    }

    private void BuildTopBar(UIBuilder ui, RectTransform topBar)
    {
        ui.NestInto(topBar);
        var parts = ui.SplitHorizontally(0.32f, 0.16f, 0.14f, 0.14f, 0.12f, 0.12f);
        foreach (var part in parts)
        {
            part.AddFixedPadding(8f);
        }

        ui.NestInto(parts[0]);
        _search = PromptField(ui, "search.placeholder");
        _search.Editor.Target.LocalEditingChanged += _ =>
        {
            var text = _search.TargetString ?? "";
            _actions.SearchTextChanged(text);
            UpdateSearchSuggestions(text);
        };
        ui.NestOut();

        ui.NestInto(parts[1]);
        var sort = ui.Button((LocaleString)"");
        _sortButtonLabel = sort.Slot.GetComponentInChildren<Text>();
        Fix(_sortButtonLabel, TypeScale.Label);
        _sortBtn = sort;
        sort.LocalPressed += (_, _) => ToggleMenu(_sortMenu);
        ui.NestOut();

        ui.NestInto(parts[2]);
        var filters = ui.Button((LocaleString)"");
        _filtersButtonLabel = filters.Slot.GetComponentInChildren<Text>();
        Fix(_filtersButtonLabel, TypeScale.Label);
        _filtersBtn = filters;
        filters.LocalPressed += (_, _) => ToggleMenu(_filtersMenu);
        ui.NestOut();

        ui.NestInto(parts[3]);
        var presets = ui.Button((LocaleString)(UiStrings.Get("bar.presets") + " ▾"));
        Fix(presets.Slot.GetComponentInChildren<Text>(), TypeScale.Label);
        _presetsBtn = presets;
        presets.LocalPressed += (_, _) => ToggleMenu(_presetsMenu);
        ui.NestOut();

        ui.NestInto(parts[4]);
        var refresh = ui.Button((LocaleString)UiStrings.Get("bar.refresh"));
        Fix(refresh.Slot.GetComponentInChildren<Text>(), TypeScale.Label);
        refresh.LocalPressed += (_, _) => _actions.Refresh();
        ui.NestOut();

        ui.NestInto(parts[5]);
        var settings = ui.Button((LocaleString)UiStrings.Get("bar.settings"), Theme.ButtonQuiet);
        Fix(settings.Slot.GetComponentInChildren<Text>(), TypeScale.Label);
        settings.LocalPressed += (_, _) => _actions.OpenSettings();
        ui.NestOut();

        ui.NestOut();
    }

    private void BuildChipBar(UIBuilder ui, RectTransform chipBar)
    {
        ui.NestInto(chipBar);
        chipBar.AddFixedPadding(6f);
        var row = ui.HorizontalLayout(8f, 4f, Alignment.MiddleLeft);
        row.ForceExpandWidth.Value = false;
        ui.Style.MinHeight = 40f;
        ui.Style.MinWidth = 96f;
        for (var i = 0; i < MaxChips; i++)
        {
            var chip = ui.Button((LocaleString)"");
            var label = chip.Slot.GetComponentInChildren<Text>();
            Fix(label, TypeScale.Body);
            chip.Slot.ActiveSelf = false;
            var index = i;
            chip.LocalPressed += (_, _) => ChipPressed(index);
            _chips.Add((chip, label));
        }

        _clearChips = ui.Button((LocaleString)UiStrings.Get("chips.clear"), Theme.ButtonQuiet);
        _clearChips.Slot.ActiveSelf = false;
        _clearChips.LocalPressed += (_, _) => _actions.ClearChips();
        ui.Style.MinWidth = 0f;
        ui.NestOut();
        ui.NestOut();
    }

    private string?[] _chipTags = new string?[MaxChips];

    private void ChipPressed(int index)
    {
        if (_chipTags[index] is { } tag)
        {
            _actions.CycleChip(tag);
        }
    }

    private GridLayout _grid = null!;
    private bool _listLayout;

    private void BuildHeader(UIBuilder ui, RectTransform header)
    {
        header.AddFixedPadding(6f);
        ui.NestInto(header);
        var parts = ui.SplitHorizontally(0.25f, 0.28f, 0.17f, 0.30f);
        _title = TextIn(ui, parts[0], Alignment.MiddleLeft, Theme.Heading, 26f, 44f);
        _subtitle = TextIn(ui, parts[1], Alignment.MiddleLeft, Theme.TextSecondary, 16f, 22f);
        Fix(_subtitle, TypeScale.Body);
        _count = TextIn(ui, parts[2], Alignment.MiddleRight, Theme.TextSecondary, 16f, 24f);

        // Tools for a named list (rename, move, delete); shown only on a list.
        ui.NestInto(parts[3]);
        var tools = ui.HorizontalLayout(6f, 4f, Alignment.MiddleRight);
        tools.ForceExpandWidth.Value = false;
        ui.Style.MinHeight = 44f;
        ui.Style.MinWidth = 62f;
        foreach (var (tool, id) in new[] { (ListTool.Rename, "list.rename"), (ListTool.MoveUp, "list.moveUp"), (ListTool.MoveDown, "list.moveDown"), (ListTool.Delete, "list.delete") })
        {
            var button = ui.Button((LocaleString)UiStrings.Get(id), Theme.ButtonQuiet);
            Fix(button.Slot.GetComponentInChildren<Text>(), TypeScale.Caption);
            var captured = tool;
            button.LocalPressed += (_, _) => _actions.ListToolPressed(captured);
        }

        ui.Style.MinWidth = 0f;
        _listTools = tools.Slot;
        _listTools.ActiveSelf = false;
        ui.NestOut();
        ui.NestOut();
        ui.NestOut();
    }

    private void BuildFooter(UIBuilder ui, RectTransform footer)
    {
        footer.AddFixedPadding(6f);
        ui.NestInto(footer);
        var parts = ui.SplitHorizontally(0.44f, 0.16f, 0.20f, 0.20f);
        _status = TextIn(ui, parts[0], Alignment.MiddleLeft, Theme.TextSecondary, 14f, 20f);
        parts[1].AddFixedPadding(4f);
        parts[2].AddFixedPadding(4f);
        parts[3].AddFixedPadding(4f);

        ui.NestInto(parts[1]);
        var classic = ui.Button((LocaleString)UiStrings.Get("bar.classic"), Theme.ButtonQuiet);
        Fix(classic.Slot.GetComponentInChildren<Text>(), TypeScale.Body);
        classic.LocalPressed += (_, _) => _actions.OpenClassicBrowser();
        ui.NestOut();

        ui.NestInto(parts[2]);
        _previous = ui.Button((LocaleString)UiStrings.Get("nav.previous"));
        _previous.LocalPressed += (_, _) => _actions.PreviousPage();
        ui.NestOut();

        ui.NestInto(parts[3]);
        _next = ui.Button((LocaleString)UiStrings.Get("nav.next"), Theme.ButtonSelected);
        _next.LocalPressed += (_, _) => _actions.NextPage();
        ui.NestOut();
        ui.NestOut();
    }

    private void BuildBody(UIBuilder ui, RectTransform body)
    {
        ui.NestInto(body);

        // An error banner row above the grid. Its height is 0 when hidden, so the grid simply takes the space.
        ui.HorizontalHeader(0f, out _bannerRect, out _bodyRect);
        ui.NestInto(_bannerRect);
        ui.Panel(Theme.Banner, _rounded, NineSliceSizing.FixedSize);
        var bannerParts = ui.SplitHorizontally(0.80f, 0.20f);
        _bannerText = TextIn(ui, bannerParts[0], Alignment.MiddleLeft, Theme.Text, 14f, 20f);
        ui.NestInto(bannerParts[1]);
        var retry = ui.Button((LocaleString)UiStrings.Get("banner.retry"), Theme.ButtonSelected);
        retry.LocalPressed += (_, _) => _actions.Retry();
        ui.NestOut();
        _banner = _bannerRect.Slot;
        _banner.ActiveSelf = false;
        ui.NestOut();
        ui.NestOut();

        ui.NestInto(_bodyRect);
        var viewport = ui.Next("Results");
        ScrollRect.CreateScrollRect(viewport, out var content);
        _viewport = viewport.GetComponent<RectTransform>();
        _viewport.AddFixedPadding(8f);
        _viewportMin = _viewport.OffsetMin.Value;
        _viewportMax = _viewport.OffsetMax.Value;

        var grid = content.AttachComponent<GridLayout>();
        grid.ChildAlignment = Alignment.TopLeft;
        grid.Spacing.Value = Spacing;
        grid.CellSize.Value = CellSize;
        grid.ExpandWidthToFit.Value = true;
        grid.PreserveAspectOnExpand.Value = true;
        _grid = grid;
        var fitter = content.AttachComponent<ContentSizeFitter>();
        fitter.VerticalFit.Value = SizeFit.PreferredSize;
        fitter.HorizontalFit.Value = SizeFit.Disabled;
        _content = content;

        // Empty state, centred over the grid; hidden while there are cards.
        var area = ui.Empty("EmptyState");
        var areaRect = area.GetComponent<RectTransform>();
        areaRect.AnchorMin.Value = new float2(0.15f, 0.30f);
        areaRect.AnchorMax.Value = new float2(0.85f, 0.70f);
        _emptyArea = area;
        ui.NestInto(areaRect);
        var split = ui.SplitVertically(0.62f, 0.38f);
        _emptyText = TextIn(ui, split[0], Alignment.MiddleCenter, Theme.TextSecondary, 20f, 30f);
        ui.NestInto(split[1]);
        split[1].AddFixedPadding(6f);
        var clear = ui.Button((LocaleString)UiStrings.Get("empty.clearFilters"), Theme.ButtonSelected);
        clear.LocalPressed += (_, _) => _actions.ClearChips();
        _clearFiltersButton = clear.Slot;
        ui.NestOut();
        ui.NestOut();
        _emptyArea.ActiveSelf = false;
        ui.NestOut(); // _bodyRect
        ui.NestOut(); // body
    }

    private void BuildPopovers(UIBuilder ui, RectTransform main)
    {
        // A click-catcher behind the popovers: pressing outside closes them. It must not dim the pane (that was jarring),
        // and a Button drives its Image's tint through its own colour driver, so the driver is set to the same colour.
        _scrim = Catcher(ui, "Scrim", Theme.Scrim, CloseMenus);

        // Centred under the button that opens each (fractions of the pane right of the rail).
        _sortMenu = BuildMenu(ui, new float2(0.32f, 0.26f), new float2(0.62f, 0.90f), BuildSortMenu);
        _presetsMenu = BuildMenu(ui, new float2(0.51f, 0.06f), new float2(0.87f, 0.90f), BuildPresetsMenu);
        _filtersMenu = BuildMenu(ui, new float2(0.42f, 0.06f), new float2(0.87f, 0.90f), BuildFiltersMenu);
        _menuAnchors[_sortMenu] = _sortBtn;
        _menuAnchors[_presetsMenu] = _presetsBtn;
        _menuAnchors[_filtersMenu] = _filtersBtn;
        _suggestMenu = BuildMenu(ui, new float2(0.02f, 0.58f), new float2(0.40f, 0.90f), BuildSuggestMenu);
        BuildConfirm(ui);
        BuildNewSession(ui);
        BuildOrganize(ui);
        BuildPrompt(ui);
        BuildSettings(ui);
        BuildDetails(ui);
    }

    /// <summary>
    /// Makes a popup panel take presses on its empty parts. Without a button under the pointer the press falls through to
    /// the scrim behind the panel, which closes it; the panel's own buttons sit on top and still get their presses.
    /// </summary>
    private static void Absorb(Image panel)
    {
        var button = panel.Slot.AttachComponent<Button>();
        var driver = button.ColorDrivers[0];
        driver.TintColorMode.Value = InteractionElement.ColorMode.Direct;
        driver.NormalColor.Value = Theme.Rail;
        driver.HighlightColor.Value = Theme.Rail;
        driver.PressColor.Value = Theme.Rail;
        driver.DisabledColor.Value = Theme.Rail;
    }

    /// <summary>A full-pane press target. Hidden until needed; its colour is fixed so the button cannot recolour it.</summary>
    private static Slot Catcher(UIBuilder ui, string name, colorX color, Action onPressed)
    {
        var image = ui.Image(color);
        image.Slot.Name = name;
        var button = image.Slot.AttachComponent<Button>();

        // Not SetColors: it derives the hover and press colours from the base colour through ColorHSV, which has alpha 1,
        // so a see-through catcher turned opaque grey whenever the pointer was over it (always, since it covers the
        // pane). Direct mode with all four colours equal keeps it exactly as drawn.
        var driver = button.ColorDrivers[0];
        driver.TintColorMode.Value = InteractionElement.ColorMode.Direct;
        driver.NormalColor.Value = color;
        driver.HighlightColor.Value = color;
        driver.PressColor.Value = color;
        driver.DisabledColor.Value = color;
        button.LocalPressed += (_, _) => onPressed();
        image.Slot.ActiveSelf = false;
        return image.Slot;
    }

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
    }

    // ---- new session panel (modelled on the stock Session Settings dialog: who can join, then Start Session)

    private Slot _newSessionPanel = null!;
    private Text _newSessionWorld = null!;
    private Text _newSessionMobile = null!;
    private Button _newSessionMobileButton = null!;
    private readonly List<(SkyFrost.Base.SessionAccessLevel? Level, Button Button, Text Label)> _newSessionLevels = [];
    private SkyFrost.Base.SessionAccessLevel? _newSessionLevel;
    private bool _newSessionMobileFriendly;
    private Action<SkyFrost.Base.SessionAccessLevel?, bool>? _newSessionStart;

    private void BuildNewSession(UIBuilder ui)
    {
        var slot = BeginFloating(ui, "NewSession", 0.32f, 0.68f, 0.5f, 0.5f, 6f, 20f);

        ui.Style.MinHeight = 48f;
        var title = ui.Text((LocaleString)UiStrings.Get("newSession.title"), bestFit: true, alignment: Alignment.MiddleCenter);
        Fix(title, TypeScale.Title);
        title.Color.Value = Theme.Heading;

        ui.Style.MinHeight = 40f;
        _newSessionWorld = ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.MiddleCenter);
        Fix(_newSessionWorld, TypeScale.Body);
        _newSessionWorld.Color.Value = Theme.TextSecondary;

        ui.Style.MinHeight = 36f;
        var who = ui.Text((LocaleString)UiStrings.Get("newSession.who"), bestFit: true, alignment: Alignment.BottomLeft);
        Fix(who, TypeScale.Body);
        who.Color.Value = Theme.TextSecondary;

        ui.Style.MinHeight = 42f;
        SkyFrost.Base.SessionAccessLevel?[] levels =
        [
            null,
            SkyFrost.Base.SessionAccessLevel.Anyone,
            SkyFrost.Base.SessionAccessLevel.RegisteredUsers,
            SkyFrost.Base.SessionAccessLevel.ContactsPlus,
            SkyFrost.Base.SessionAccessLevel.Contacts,
            SkyFrost.Base.SessionAccessLevel.LAN,
            SkyFrost.Base.SessionAccessLevel.Private,
        ];
        foreach (var level in levels)
        {
            var button = ui.Button((LocaleString)"");
            var label = button.Slot.GetComponentInChildren<Text>();
            Fix(label, TypeScale.Body);
            var captured = level;
            button.LocalPressed += (_, _) =>
            {
                _newSessionLevel = captured;
                RefreshNewSession();
            };
            _newSessionLevels.Add((level, button, label));
        }

        ui.Style.MinHeight = 48f;
        _newSessionMobileButton = ui.Button((LocaleString)"");
        _newSessionMobile = _newSessionMobileButton.Slot.GetComponentInChildren<Text>();
        Fix(_newSessionMobile, TypeScale.Body);
        _newSessionMobileButton.LocalPressed += (_, _) =>
        {
            _newSessionMobileFriendly = !_newSessionMobileFriendly;
            RefreshNewSession();
        };

        ui.Style.MinHeight = 56f;
        var row = ui.HorizontalLayout(10f, 0f, Alignment.MiddleCenter);
        var start = ui.Button((LocaleString)UiStrings.Get("newSession.start"), Theme.ButtonSelected);
        Fix(start.Slot.GetComponentInChildren<Text>(), TypeScale.Label);
        start.LocalPressed += (_, _) =>
        {
            var action = _newSessionStart;
            var level = _newSessionLevel;
            var mobile = _newSessionMobileFriendly;
            CloseMenus();
            action?.Invoke(level, mobile);
        };
        var cancel = ui.Button((LocaleString)UiStrings.Get("confirm.cancel"), Theme.ButtonQuiet);
        Fix(cancel.Slot.GetComponentInChildren<Text>(), TypeScale.Label);
        cancel.LocalPressed += (_, _) => CloseMenus();
        ui.NestOut();

        ui.NestOut();
        slot.ActiveSelf = false;
        _newSessionPanel = slot;
    }

    private void RefreshNewSession()
    {
        foreach (var (level, button, label) in _newSessionLevels)
        {
            var on = level == _newSessionLevel;
            SetColor(button, on ? Theme.ButtonSelected : Theme.Button);
            var name = level is null ? UiStrings.Get("newSession.default") : UiStrings.Get("access." + level);
            label.Content.Value = "<nobr>" + (on ? "✓  " : "      ") + name + "</nobr>";
        }

        SetColor(_newSessionMobileButton, _newSessionMobileFriendly ? Theme.ButtonSelected : Theme.Button);
        _newSessionMobile.Content.Value = "<nobr>" + (_newSessionMobileFriendly ? "✓  " : "      ") + UiStrings.Get("newSession.mobile") + "</nobr>";
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
            _presetRows[i].Label.Content.Value = show ? "<nobr>" + Plain(names[i]) + "</nobr>" : "";
        }
    }

    private void BuildSortMenu(UIBuilder ui)
    {
        var title = ui.Text((LocaleString)UiStrings.Get("sort.title"), bestFit: true, alignment: Alignment.MiddleLeft);
        Fix(title, TypeScale.Body);
        title.Color.Value = Theme.TextSecondary;

        ui.Style.MinHeight = 40f; // ten sorts have to fit the panel
        foreach (var key in Sorting.All)
        {
            var button = ui.Button((LocaleString)"");
            var captured = key;
            button.LocalPressed += (_, _) => { _actions.ChooseSort(captured); CloseMenus(); };
            _sortItems[key] = (button, button.Slot.GetComponentInChildren<Text>());
        }

        ui.Style.MinHeight = 50f;
        var row = ui.HorizontalLayout(8f, 0f, Alignment.MiddleCenter);
        _descending = ui.Button((LocaleString)UiStrings.Get("sort.descending"));
        _descending.LocalPressed += (_, _) => _actions.SetDescending(true);
        _ascending = ui.Button((LocaleString)UiStrings.Get("sort.ascending"));
        _ascending.LocalPressed += (_, _) => _actions.SetDescending(false);
        ui.NestOut();
    }

    private void BuildFiltersMenu(UIBuilder ui)
    {
        var head = ui.HorizontalLayout(8f, 0f, Alignment.MiddleLeft);
        var title = ui.Text((LocaleString)UiStrings.Get("filters.title"), bestFit: true, alignment: Alignment.MiddleLeft);
        Fix(title, TypeScale.Title);
        title.Color.Value = Theme.Heading;
        var reset = ui.Button((LocaleString)UiStrings.Get("filters.reset"), Theme.ButtonQuiet);
        reset.LocalPressed += (_, _) => _actions.Filter(FilterOp.Reset);
        ui.NestOut();

        Stepper(ui, FilterOp.MinUsersDown, FilterOp.MinUsersUp);
        Stepper(ui, FilterOp.MinVisitsDown, FilterOp.MinVisitsUp);
        Stepper(ui, FilterOp.MinUptimeDown, FilterOp.MinUptimeUp);

        // Two toggles to a row keeps the popover inside its fixed height.
        var pairs = new[]
        {
            (FilterOp.ToggleHasRoom, FilterOp.ToggleMobile),
            (FilterOp.ToggleVr, FilterOp.ToggleOtherVersions),
            (FilterOp.ToggleFeatured, FilterOp.ToggleIncludeEmpty),
        };
        foreach (var (first, second) in pairs)
        {
            ui.HorizontalLayout(8f, 0f, Alignment.MiddleCenter);
            foreach (var op in new[] { first, second })
            {
                var button = ui.Button((LocaleString)"");
                Fix(button.Slot.GetComponentInChildren<Text>(), TypeScale.Caption);
                var captured = op;
                button.LocalPressed += (_, _) => _actions.Filter(captured);
                _filterItems[op] = (button, button.Slot.GetComponentInChildren<Text>());
            }

            ui.NestOut();
        }

        var host = ui.HorizontalLayout(8f, 0f, Alignment.MiddleCenter);
        foreach (var (op, id) in new[] { (FilterOp.HostAny, "filters.host.any"), (FilterOp.HostPerson, "filters.host.person"), (FilterOp.HostHeadless, "filters.host.headless") })
        {
            var button = ui.Button((LocaleString)UiStrings.Get(id));
            var captured = op;
            button.LocalPressed += (_, _) => _actions.Filter(captured);
            _filterItems[op] = (button, button.Slot.GetComponentInChildren<Text>());
        }

        ui.NestOut();
        var done = ui.Button((LocaleString)UiStrings.Get("filters.done"), Theme.ButtonSelected);
        done.LocalPressed += (_, _) => CloseMenus();
    }

    /// <summary>A "− label +" row: the middle button shows the value and does nothing.</summary>
    private void Stepper(UIBuilder ui, FilterOp down, FilterOp up)
    {
        var row = ui.HorizontalLayout(8f, 0f, Alignment.MiddleCenter);
        ui.Style.MinWidth = 56f;
        var minus = ui.Button((LocaleString)"−");
        minus.LocalPressed += (_, _) => _actions.Filter(down);
        ui.Style.MinWidth = 0f;
        ui.Style.FlexibleWidth = 1f;
        var label = ui.Button((LocaleString)"", Theme.ButtonQuiet);
        ui.Style.FlexibleWidth = -1f;
        ui.Style.MinWidth = 56f;
        var plus = ui.Button((LocaleString)"+");
        plus.LocalPressed += (_, _) => _actions.Filter(up);
        ui.Style.MinWidth = 0f;
        ui.NestOut();
        _filterItems[up] = (label, label.Slot.GetComponentInChildren<Text>());
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
            UniLog.Log($"[Discover] Popover placed: centre={centre:F2} left={left:F2} top={top:F2}");
        }
        catch (Exception ex)
        {
            UniLog.Warning($"[Discover] Placing a popover failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

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
        _settingsScrim.ActiveSelf = false;
        _detailPanel.ActiveSelf = false;
        _detailScrim.ActiveSelf = false;
        ClosePrompt();
    }

    // ---- organize panel: one world's lists and personal tags

    private const int MaxOrganizeLists = 6;
    private const int MaxOrganizeTags = 5;
    private Slot _organizeScrim = null!;
    private Slot _organizePanel = null!;
    private Text _organizeTitle = null!;
    private Text _organizeNoLists = null!;
    private Text _organizeNoTags = null!;
    private TextField _tagField = null!;
    private readonly List<(Button Button, Text Label)> _organizeLists = [];
    private readonly string?[] _organizeListIds = new string?[MaxOrganizeLists];
    private readonly List<(Button Button, Text Label)> _organizeTags = [];
    private readonly string?[] _organizeTagNames = new string?[MaxOrganizeTags];

    private void BuildOrganize(UIBuilder ui)
    {
        _organizeScrim = Catcher(ui, "OrganizeScrim", Theme.ConfirmScrim, CloseMenus);

        var slot = ui.Empty("Organize");
        var rect = slot.GetComponent<RectTransform>();
        rect.AnchorMin.Value = new float2(0.20f, 0.06f);
        rect.AnchorMax.Value = new float2(0.80f, 0.94f);
        ui.NestInto(rect);
        Absorb(ui.Panel(Theme.Rail, _rounded, NineSliceSizing.FixedSize));
        var layout = ui.VerticalLayout(8f, 20f, Alignment.TopCenter);
        layout.ForceExpandHeight.Value = false;

        ui.Style.MinHeight = 50f;
        _organizeTitle = ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.MiddleCenter);
        Fix(_organizeTitle, TypeScale.Title);
        _organizeTitle.Color.Value = Theme.Heading;

        ui.Style.MinHeight = 28f;
        var lists = ui.Text((LocaleString)UiStrings.Get("organize.lists"), bestFit: true, alignment: Alignment.BottomLeft);
        Fix(lists, TypeScale.Body);
        lists.Color.Value = Theme.TextSecondary;

        ui.Style.MinHeight = 40f;
        _organizeNoLists = ui.Text((LocaleString)UiStrings.Get("organize.noLists"), bestFit: true, alignment: Alignment.MiddleLeft);
        Fix(_organizeNoLists, TypeScale.Body);
        _organizeNoLists.Color.Value = Theme.TextSecondary;

        ui.Style.MinHeight = 42f;
        for (var i = 0; i < MaxOrganizeLists; i++)
        {
            var button = ui.Button((LocaleString)"");
            var label = button.Slot.GetComponentInChildren<Text>();
            Fix(label, TypeScale.Body);
            var index = i;
            button.LocalPressed += (_, _) =>
            {
                if (_organizeListIds[index] is { } id)
                {
                    _actions.OrganizeToggleList(id);
                }
            };
            button.Slot.ActiveSelf = false;
            _organizeLists.Add((button, label));
        }

        var newList = ui.Button((LocaleString)UiStrings.Get("rail.newList"), Theme.ButtonQuiet);
        Fix(newList.Slot.GetComponentInChildren<Text>(), TypeScale.Body);
        newList.LocalPressed += (_, _) => _actions.OrganizeNewList();

        ui.Style.MinHeight = 28f;
        var tags = ui.Text((LocaleString)UiStrings.Get("organize.tags"), bestFit: true, alignment: Alignment.BottomLeft);
        Fix(tags, TypeScale.Body);
        tags.Color.Value = Theme.TextSecondary;

        ui.Style.MinHeight = 38f;
        _organizeNoTags = ui.Text((LocaleString)UiStrings.Get("organize.noTags"), bestFit: true, alignment: Alignment.MiddleLeft);
        Fix(_organizeNoTags, TypeScale.Body);
        _organizeNoTags.Color.Value = Theme.TextSecondary;

        ui.Style.MinHeight = 40f;
        var tagRow = ui.HorizontalLayout(6f, 0f, Alignment.MiddleLeft);
        tagRow.ForceExpandWidth.Value = false;
        ui.Style.MinWidth = 96f;
        for (var i = 0; i < MaxOrganizeTags; i++)
        {
            var button = ui.Button((LocaleString)"", Theme.ChipPersonal);
            var label = button.Slot.GetComponentInChildren<Text>();
            Fix(label, TypeScale.Caption);
            var index = i;
            button.LocalPressed += (_, _) =>
            {
                if (_organizeTagNames[index] is { } name)
                {
                    _actions.OrganizeRemoveTag(name);
                }
            };
            button.Slot.ActiveSelf = false;
            _organizeTags.Add((button, label));
        }

        ui.Style.MinWidth = 0f;
        ui.NestOut();

        ui.Style.MinHeight = 48f;
        var addRow = ui.HorizontalLayout(8f, 0f, Alignment.MiddleCenter);
        ui.Style.FlexibleWidth = 1f;
        _tagField = PromptField(ui, "prompt.tagPlaceholder");
        _tagField.Editor.Target.LocalSubmitPressed += _ => SubmitTag();
        _tagField.Editor.Target.LocalEditingChanged += _ => RefreshTagSuggestions();
        ui.Style.FlexibleWidth = -1f;
        ui.Style.MinWidth = 96f;
        var add = ui.Button((LocaleString)UiStrings.Get("organize.add"), Theme.ButtonSelected);
        add.LocalPressed += (_, _) => SubmitTag();
        ui.Style.MinWidth = 0f;
        ui.NestOut();

        ui.Style.MinHeight = 28f;
        var suggested = ui.Text((LocaleString)UiStrings.Get("organize.suggested"), bestFit: true, alignment: Alignment.BottomLeft);
        Fix(suggested, TypeScale.Body);
        suggested.Color.Value = Theme.TextSecondary;

        ui.Style.MinHeight = 40f;
        var suggestRow = ui.HorizontalLayout(6f, 0f, Alignment.MiddleLeft);
        suggestRow.ForceExpandWidth.Value = false;
        ui.Style.MinWidth = 96f;
        for (var i = 0; i < MaxTagSuggestions; i++)
        {
            var button = ui.Button((LocaleString)"", Theme.Button);
            var label = button.Slot.GetComponentInChildren<Text>();
            Fix(label, TypeScale.Caption);
            var index = i;
            button.LocalPressed += (_, _) =>
            {
                if (_tagSuggestions[index] is { } name)
                {
                    _tagField.TargetString = null;
                    _actions.OrganizeAddTag(name);
                }
            };
            button.Slot.ActiveSelf = false;
            _tagSuggestionButtons.Add((button, label));
        }

        ui.Style.MinWidth = 0f;
        ui.NestOut();

        ui.Style.MinHeight = 50f;
        var done = ui.Button((LocaleString)UiStrings.Get("organize.done"), Theme.ButtonSelected);
        done.LocalPressed += (_, _) => CloseMenus();

        ui.NestOut();
        ui.NestOut();
        ui.NestOut();
        slot.ActiveSelf = false;
        _organizePanel = slot;
    }

    private void SubmitTag()
    {
        var text = _tagField.TargetString ?? "";
        if (!string.IsNullOrWhiteSpace(text))
        {
            _tagField.TargetString = null;
            _actions.OrganizeAddTag(text);
        }
    }

    private const int MaxTagSuggestions = 5;
    private readonly List<(Button Button, Text Label)> _tagSuggestionButtons = [];
    private readonly string?[] _tagSuggestions = new string?[MaxTagSuggestions];

    /// <summary>Tags already in use that match what is typed in the organize panel (the most used ones when it is empty).</summary>
    private void RefreshTagSuggestions()
    {
        var found = _actions.SuggestTags(_tagField.TargetString ?? "", forSearch: false);
        for (var i = 0; i < MaxTagSuggestions; i++)
        {
            var show = i < found.Count;
            var (button, label) = _tagSuggestionButtons[i];
            button.Slot.ActiveSelf = show;
            _tagSuggestions[i] = show ? found[i] : null;
            if (show)
            {
                label.Content.Value = "<nobr>+ " + Plain(CardText.Ellipsize(found[i], 12)) + "</nobr>";
            }
        }
    }

    // ---- search tag suggestions: a small popup under the search bar while the last word is a partial tag

    private Slot _suggestMenu = null!;
    private const int MaxSearchSuggestions = 4;
    private readonly List<(Button Button, Text Label)> _searchSuggestionButtons = [];
    private readonly string?[] _searchSuggestions = new string?[MaxSearchSuggestions];
    private SearchWord _suggestWord;

    private void BuildSuggestMenu(UIBuilder ui)
    {
        for (var i = 0; i < MaxSearchSuggestions; i++)
        {
            var button = ui.Button((LocaleString)"", Theme.Button);
            var label = button.Slot.GetComponentInChildren<Text>();
            Fix(label, TypeScale.Body);
            var index = i;
            button.LocalPressed += (_, _) =>
            {
                if (_searchSuggestions[index] is { } name)
                {
                    var text = TagSuggester.Complete(_suggestWord, name);
                    _search.TargetString = text;
                    _suggestMenu.ActiveSelf = false;
                    _actions.SearchTextChanged(text);
                }
            };
            button.Slot.ActiveSelf = false;
            _searchSuggestionButtons.Add((button, label));
        }
    }

    private void UpdateSearchSuggestions(string text)
    {
        var word = TagSuggester.LastWord(text);
        var found = word is { Typed.Length: > 0 } ? _actions.SuggestTags(word.Value.Typed, forSearch: true) : [];
        if (found.Count == 0)
        {
            _suggestMenu.ActiveSelf = false;
            return;
        }

        _suggestWord = word!.Value;
        for (var i = 0; i < MaxSearchSuggestions; i++)
        {
            var show = i < found.Count;
            var (button, label) = _searchSuggestionButtons[i];
            button.Slot.ActiveSelf = show;
            _searchSuggestions[i] = show ? found[i] : null;
            if (show)
            {
                label.Content.Value = "<nobr>tag:" + Plain(CardText.Ellipsize(found[i], 24)) + "</nobr>";
            }
        }

        _suggestMenu.ActiveSelf = true;
    }

    /// <summary>
    /// A text field whose prompt shows, dimmed, while it is empty. The prompt is the Text's NullContent, which only draws
    /// when Content is null, so empty fields are kept null (and put back to null when editing ends empty).
    /// </summary>
    private static TextField PromptField(UIBuilder ui, string promptId)
    {
        var field = ui.TextField(null!, undo: false, parseRTF: false, promptText: (LocaleString)("<alpha=#70>" + UiStrings.Get(promptId)));
        field.Editor.Target.LocalEditingFinished += _ =>
        {
            if (string.IsNullOrEmpty(field.TargetString))
            {
                field.TargetString = null;
            }
        };
        return field;
    }

    /// <summary>Shows or refreshes the organize panel for one world.</summary>
    public void ShowOrganize(OrganizeModel model)
    {
        _organizeTitle.Content.Value = "<nobr>" + Plain(CardText.Ellipsize(model.Title, 40)) + "</nobr>";

        _organizeNoLists.Slot.ActiveSelf = model.Lists.Count == 0;
        for (var i = 0; i < MaxOrganizeLists; i++)
        {
            var show = i < model.Lists.Count;
            var (button, label) = _organizeLists[i];
            button.Slot.ActiveSelf = show;
            _organizeListIds[i] = show ? model.Lists[i].Id : null;
            if (show)
            {
                var list = model.Lists[i];
                label.Content.Value = "<nobr>" + (list.Member ? "✓  " : "") + Plain(CardText.Ellipsize(list.Name, 28)) + "</nobr>";
                SetColor(button, list.Member ? Theme.ButtonSelected : Theme.Button);
            }
        }

        _organizeNoTags.Slot.ActiveSelf = model.Tags.Count == 0;
        for (var i = 0; i < MaxOrganizeTags; i++)
        {
            var show = i < model.Tags.Count;
            var (button, label) = _organizeTags[i];
            button.Slot.ActiveSelf = show;
            _organizeTagNames[i] = show ? model.Tags[i] : null;
            if (show)
            {
                label.Content.Value = "<nobr>× " + Plain(CardText.Ellipsize(model.Tags[i], 12)) + "</nobr>";
            }
        }

        RefreshTagSuggestions();
        _organizeScrim.ActiveSelf = true;
        _organizePanel.ActiveSelf = true;
    }

    // ---- detail panel (spec F18): everything known about one world, with Join and Lists

    private Slot _detailScrim = null!;
    private Slot _detailPanel = null!;
    private Text _detailTitle = null!;
    private Text _detailBy = null!;
    private Text _detailDescription = null!;
    private Text _detailTags = null!;
    private Text _detailSessions = null!;
    private Text _detailDevices = null!;
    private Button _detailAction = null!;
    private Text _detailActionLabel = null!;
    private Button _detailLists = null!;
    private Button _detailCopy = null!;
    private Button _detailLeave = null!;
    private Button _detailAuthor = null!;
    private Button _detailHide = null!;
    private Button _detailFavorite = null!;
    private Text _detailFavoriteLabel = null!;
    private bool _detailFavorited;
    private Text _detailLeaveLabel = null!;
    private string? _detailKey;

    private void BuildDetails(UIBuilder ui)
    {
        _detailScrim = Catcher(ui, "DetailScrim", Theme.ConfirmScrim, CloseMenus);

        var slot = ui.Empty("Detail");
        var rect = slot.GetComponent<RectTransform>();
        rect.AnchorMin.Value = new float2(0.18f, 0.06f);
        rect.AnchorMax.Value = new float2(0.82f, 0.94f);
        ui.NestInto(rect);
        Absorb(ui.Panel(Theme.Rail, _rounded, NineSliceSizing.FixedSize));
        var layout = ui.VerticalLayout(8f, 22f, Alignment.TopCenter);
        layout.ForceExpandHeight.Value = false;

        ui.Style.MinHeight = 56f;
        _detailTitle = ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.MiddleLeft);
        Fix(_detailTitle, TypeScale.Title);
        _detailTitle.Color.Value = Theme.Heading;

        ui.Style.MinHeight = 28f;
        _detailBy = ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.MiddleLeft);
        Fix(_detailBy, TypeScale.Body);
        _detailBy.Color.Value = Theme.TextSecondary;

        ui.Style.MinHeight = 110f;
        _detailDescription = ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.TopLeft);
        Fix(_detailDescription, TypeScale.Body);

        ui.Style.MinHeight = 26f;
        var tagsHeader = ui.Text((LocaleString)UiStrings.Get("detail.tags"), bestFit: true, alignment: Alignment.BottomLeft);
        Fix(tagsHeader, TypeScale.Caption);
        tagsHeader.Color.Value = Theme.TextSecondary;
        ui.Style.MinHeight = 52f;
        _detailTags = ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.TopLeft);
        Fix(_detailTags, TypeScale.Body);

        ui.Style.MinHeight = 26f;
        var sessionsHeader = ui.Text((LocaleString)UiStrings.Get("detail.sessions"), bestFit: true, alignment: Alignment.BottomLeft);
        Fix(sessionsHeader, TypeScale.Caption);
        sessionsHeader.Color.Value = Theme.TextSecondary;
        ui.Style.MinHeight = 96f;
        _detailSessions = ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.TopLeft);
        Fix(_detailSessions, TypeScale.Body);
        ui.Style.MinHeight = 26f;
        _detailDevices = ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.MiddleLeft);
        Fix(_detailDevices, TypeScale.Caption);
        _detailDevices.Color.Value = Theme.TextSecondary;

        ui.Style.MinHeight = 50f;
        var row = ui.HorizontalLayout(10f, 0f, Alignment.MiddleCenter);
        _detailAction = ui.Button((LocaleString)"", Theme.ButtonSelected);
        _detailActionLabel = _detailAction.Slot.GetComponentInChildren<Text>();
        _detailAction.LocalPressed += (_, _) =>
        {
            if (_detailKey is { } key)
            {
                CloseMenus();
                _actions.CardPressed(key);
            }
        };
        _detailFavorite = ui.Button((LocaleString)UiStrings.Get("detail.favorite"));
        _detailFavoriteLabel = _detailFavorite.Slot.GetComponentInChildren<Text>();
        _detailFavorite.LocalPressed += (_, _) =>
        {
            if (_detailKey is { } key)
            {
                _actions.ToggleFavorite(key);
                _detailFavorited = !_detailFavorited;
                _detailFavoriteLabel.Content.Value = UiStrings.Get(_detailFavorited ? "detail.unfavorite" : "detail.favorite");
            }
        };
        _detailCopy = ui.Button((LocaleString)UiStrings.Get("detail.copyLink"));
        _detailCopy.LocalPressed += (_, _) =>
        {
            if (_detailKey is { } key)
            {
                _actions.CopyLink(key);
            }
        };
        _detailLists = ui.Button((LocaleString)UiStrings.Get("card.organize"));
        _detailLists.LocalPressed += (_, _) =>
        {
            if (_detailKey is { } key)
            {
                CloseMenus();
                _actions.OpenOrganize(key);
            }
        };
        _detailAuthor = ui.Button((LocaleString)UiStrings.Get("detail.moreByAuthor"));
        _detailAuthor.LocalPressed += (_, _) =>
        {
            if (_detailKey is { } key)
            {
                CloseMenus();
                _actions.MoreByAuthor(key);
            }
        };
        _detailHide = ui.Button((LocaleString)UiStrings.Get("detail.hide"), Theme.ButtonQuiet);
        _detailHide.LocalPressed += (_, _) =>
        {
            if (_detailKey is { } key)
            {
                CloseMenus();
                _actions.HideOptions(key);
            }
        };
        _detailLeave = ui.Button((LocaleString)UiStrings.Get("detail.leave"), Theme.ButtonQuiet);
        _detailLeaveLabel = _detailLeave.Slot.GetComponentInChildren<Text>();
        _detailLeave.LocalPressed += (_, _) =>
        {
            if (_detailKey is { } key)
            {
                CloseMenus();
                _actions.LeaveWorld(key);
            }
        };
        _detailLeave.Slot.ActiveSelf = false;
        var close = ui.Button((LocaleString)UiStrings.Get("detail.close"), Theme.ButtonQuiet);
        close.LocalPressed += (_, _) => CloseMenus();
        ui.NestOut();

        ui.NestOut();
        ui.NestOut();
        ui.NestOut();
        slot.ActiveSelf = false;
        _detailPanel = slot;
    }

    public void ShowDetails(DetailModel model)
    {
        CloseMenus();
        _detailKey = model.Key;
        _detailTitle.Content.Value = "<nobr>" + Plain(model.Title) + "</nobr>";
        _detailBy.Content.Value = "<nobr>" + Plain(model.ByLine) + "</nobr>";
        _detailDescription.Content.Value = Plain(model.Description);
        _detailTags.Content.Value = Plain(model.Tags);
        _detailSessions.Content.Value = Plain(model.Sessions);
        _detailDevices.Content.Value = Plain(model.Devices);
        _detailLists.Slot.ActiveSelf = model.CanSave;
        _detailCopy.Slot.ActiveSelf = model.CanCopyLink;
        _detailAuthor.Slot.ActiveSelf = model.CanMoreByAuthor;
        _detailHide.Slot.ActiveSelf = model.CanSave;
        _detailFavorite.Slot.ActiveSelf = model.CanSave;
        _detailFavorited = model.IsFavorite;
        _detailFavoriteLabel.Content.Value = UiStrings.Get(model.IsFavorite ? "detail.unfavorite" : "detail.favorite");
        _detailLeave.Slot.ActiveSelf = model.Leave != LeaveKind.None;
        _detailLeaveLabel.Content.Value = UiStrings.Get(model.Leave == LeaveKind.End ? "detail.endSession" : "detail.leave");
        _detailActionLabel.Content.Value = UiStrings.Get(model.Action switch
        {
            CardAction.GoThere => "card.goThere",
            CardAction.NewSession => "card.newSession",
            CardAction.Full => "card.full",
            CardAction.Offline => "card.offline",
            CardAction.OtherVersion => "card.otherVersion",
            _ => "card.join",
        });
        var enabled = model.Action is CardAction.Join or CardAction.GoThere or CardAction.NewSession;
        _detailAction.Enabled = enabled;
        _detailAction.ColorDrivers[0].SetColors(enabled ? Theme.ButtonSelected : Theme.ButtonQuiet);
        _detailScrim.ActiveSelf = true;
        _detailPanel.ActiveSelf = true;
    }

    // ---- settings panel: the sidebar's sections and lenses on the left, general options and your data on the right

    private Slot _settingsScrim = null!;
    private Slot _settingsPanel = null!;
    private Text _animationsLabel = null!;
    private Text _liveOnlyLabel = null!;
    private Text _startLensLabel = null!;
    private Text _viewModeLabel = null!;
    private readonly Dictionary<RailSectionId, (Button Button, Text Label)> _sectionRows = [];
    private readonly Dictionary<LensKind, (Button Button, Text Label)> _lensRows = [];

    private void BuildSettings(UIBuilder ui)
    {
        _settingsScrim = Catcher(ui, "SettingsScrim", Theme.ConfirmScrim, CloseMenus);

        var slot = ui.Empty("Settings");
        var rect = slot.GetComponent<RectTransform>();
        rect.AnchorMin.Value = new float2(0.10f, 0.03f);
        rect.AnchorMax.Value = new float2(0.90f, 0.97f);
        ui.NestInto(rect);
        Absorb(ui.Panel(Theme.Rail, _rounded, NineSliceSizing.FixedSize));
        var layout = ui.VerticalLayout(8f, 20f, Alignment.TopCenter);
        layout.ForceExpandHeight.Value = false;

        ui.Style.MinHeight = 48f;
        var title = ui.Text((LocaleString)UiStrings.Get("settings.title"), bestFit: true, alignment: Alignment.MiddleCenter);
        Fix(title, TypeScale.Title);
        title.Color.Value = Theme.Heading;

        ui.Style.MinHeight = 0f;
        ui.Style.FlexibleHeight = 1f;
        var columns = ui.HorizontalLayout(18f, 0f, Alignment.TopLeft);
        columns.ForceExpandHeight.Value = false;
        ui.Style.FlexibleHeight = -1f;

        // Left: one switch per section, and one per lens under it.
        ui.Style.FlexibleWidth = 1f;
        var left = ui.VerticalLayout(5f, 0f, Alignment.TopCenter);
        left.ForceExpandHeight.Value = false;
        ui.Style.FlexibleWidth = -1f;
        SettingsHeading(ui, UiStrings.Get("settings.sidebar"));
        foreach (var section in LensCatalog.Sections)
        {
            ui.Style.MinHeight = 36f;
            var id = section;
            _sectionRows[section] = SettingsRow(ui, () => _actions.ToggleSection(id));
            foreach (var lens in LensCatalog.LensesOf(section))
            {
                ui.Style.MinHeight = 32f;
                var kind = lens;
                _lensRows[lens] = SettingsRow(ui, () => _actions.ToggleLens(kind));
            }
        }

        ui.NestOut();

        // Right: general options and the export and import buttons.
        ui.Style.FlexibleWidth = 1f;
        var right = ui.VerticalLayout(8f, 0f, Alignment.TopCenter);
        right.ForceExpandHeight.Value = false;
        ui.Style.FlexibleWidth = -1f;
        SettingsHeading(ui, UiStrings.Get("settings.general"));
        ui.Style.MinHeight = 44f;
        _animationsLabel = SettingsRow(ui, () => _actions.ToggleSetting(SettingKey.Animations)).Label;
        _liveOnlyLabel = SettingsRow(ui, () => _actions.ToggleSetting(SettingKey.LiveOnly)).Label;
        _startLensLabel = SettingsRow(ui, () => _actions.ToggleSetting(SettingKey.StartLens)).Label;
        _viewModeLabel = SettingsRow(ui, () => _actions.ToggleSetting(SettingKey.ListView)).Label;
        _cardsPerRowLabel = SettingsRow(ui, () => _actions.ToggleSetting(SettingKey.CardsPerRow)).Label;
        _notifyLiveLabel = SettingsRow(ui, () => _actions.ToggleSetting(SettingKey.NotifyLive)).Label;
        _notifyContactsLabel = SettingsRow(ui, () => _actions.ToggleSetting(SettingKey.NotifyContacts)).Label;
        _hiddenRestoreLabel = SettingsRow(ui, () => _actions.ToggleSetting(SettingKey.HiddenRestore)).Label;
        _hiddenWordLabel = SettingsRow(ui, () => _actions.ToggleSetting(SettingKey.HiddenWord)).Label;

        SettingsHeading(ui, UiStrings.Get("settings.data"));
        ui.Style.MinHeight = 44f;
        var row = ui.HorizontalLayout(10f, 0f, Alignment.MiddleCenter);
        var export = ui.Button((LocaleString)UiStrings.Get("settings.export"));
        export.LocalPressed += (_, _) => _actions.ExportLibrary();
        var import = ui.Button((LocaleString)UiStrings.Get("settings.import"));
        import.LocalPressed += (_, _) => _actions.ImportLibrary();
        ui.NestOut();

        ui.Style.MinHeight = 90f;
        var note = ui.Text((LocaleString)UiStrings.Get("settings.note"), bestFit: true, alignment: Alignment.TopLeft);
        Fix(note, TypeScale.Caption);
        note.Color.Value = Theme.TextSecondary;
        ui.NestOut();

        ui.NestOut(); // columns

        ui.Style.MinHeight = 50f;
        var done = ui.Button((LocaleString)UiStrings.Get("organize.done"), Theme.ButtonSelected);
        done.LocalPressed += (_, _) => CloseMenus();

        ui.NestOut();
        ui.NestOut();
        ui.NestOut();
        slot.ActiveSelf = false;
        _settingsPanel = slot;
    }

    private static void SettingsHeading(UIBuilder ui, string text)
    {
        ui.Style.MinHeight = 30f;
        var heading = ui.Text((LocaleString)text, bestFit: true, alignment: Alignment.BottomLeft);
        Fix(heading, TypeScale.Body);
        heading.Color.Value = Theme.TextSecondary;
    }

    private (Button Button, Text Label) SettingsRow(UIBuilder ui, Action pressed)
    {
        var button = ui.Button((LocaleString)"");
        var label = button.Slot.GetComponentInChildren<Text>();
        Fix(label, TypeScale.Body);
        button.LocalPressed += (_, _) => pressed();
        return (button, label);
    }

    public void ShowSettings(SettingsModel model)
    {
        string OnOff(bool on) => UiStrings.Get(on ? "settings.on" : "settings.off");
        var settings = model.Settings;

        _animationsLabel.Content.Value = $"<nobr>{UiStrings.Get("settings.animations")}:  {OnOff(model.Animations)}</nobr>";
        _liveOnlyLabel.Content.Value = $"<nobr>{UiStrings.Get("settings.liveOnly")}:  {OnOff(model.LiveOnly)}</nobr>";
        _startLensLabel.Content.Value = $"<nobr>{UiStrings.Get("settings.startOn")}:  {Plain(model.StartLens)}</nobr>";
        _viewModeLabel.Content.Value = $"<nobr>{UiStrings.Get("settings.view")}:  {UiStrings.Get(settings.ListView ? "settings.viewList" : "settings.viewGrid")}</nobr>";

        _cardsPerRowLabel.Content.Value = $"<nobr>{UiStrings.Get("settings.cardsPerRow")}:  {(settings.CardsPerRow > 0 ? settings.CardsPerRow : 4)}</nobr>";

        _notifyLiveLabel.Content.Value = $"<nobr>{UiStrings.Get("settings.notifyLive")}:  {OnOff(settings.NotifyFavoriteLive)}</nobr>";
        _notifyContactsLabel.Content.Value = $"<nobr>{UiStrings.Get("settings.notifyContacts")}:  {OnOff(settings.NotifyContactsInFavorite)}</nobr>";
        _hiddenRestoreLabel.Content.Value = "<nobr>" + UiStrings.Format("settings.hiddenRestore", settings.HiddenWorlds.Count, settings.HiddenAuthors.Count, settings.HiddenWords.Count) + "</nobr>";
        _hiddenWordLabel.Content.Value = "<nobr>" + UiStrings.Get("settings.hiddenWord") + "</nobr>";

        foreach (var (section, (button, label)) in _sectionRows)
        {
            var on = settings.IsSectionOn(section);
            label.Content.Value = $"<nobr>{SectionName(section)}:  {OnOff(on)}</nobr>";
            SetColor(button, on ? Theme.ButtonSelected : Theme.ButtonQuiet);
        }

        foreach (var (lens, (button, label)) in _lensRows)
        {
            var switchOn = !settings.HiddenLenses.Contains(lens);
            var shown = settings.IsVisible(lens);
            label.Content.Value = $"<nobr>      {Plain(LensName(lens))}:  {OnOff(switchOn)}</nobr>";
            SetColor(button, shown ? Theme.ButtonSelected : switchOn ? Theme.Button : Theme.ButtonQuiet);
        }

        if (!_settingsPanel.ActiveSelf)
        {
            CloseMenus();
            _settingsScrim.ActiveSelf = true;
            _settingsPanel.ActiveSelf = true;
        }
    }

    private static string SectionName(RailSectionId section) => UiStrings.Get(section switch
    {
        RailSectionId.Now => "rail.now",
        RailSectionId.Discover => "rail.discover",
        RailSectionId.Insights => "rail.insights",
        _ => "rail.yours",
    });

    private static string LensName(LensKind lens) => lens == LensKind.NamedList ? UiStrings.Get("settings.yourLists") : RailBuilder.Title(lens);

    // ---- name prompt: a title, one text field, OK and Cancel (new list, rename list)

    private Slot _promptScrim = null!;
    private Slot _promptPanel = null!;
    private Text _promptTitle = null!;
    private TextField _promptField = null!;
    private Action<string>? _promptAction;

    private void BuildPrompt(UIBuilder ui)
    {
        _promptScrim = Catcher(ui, "PromptScrim", Theme.ConfirmScrim, ClosePrompt);

        var slot = ui.Empty("Prompt");
        var rect = slot.GetComponent<RectTransform>();
        rect.AnchorMin.Value = new float2(0.28f, 0.34f);
        rect.AnchorMax.Value = new float2(0.72f, 0.66f);
        ui.NestInto(rect);
        Absorb(ui.Panel(Theme.Rail, _rounded, NineSliceSizing.FixedSize));
        var layout = ui.VerticalLayout(12f, 22f, Alignment.TopCenter);
        layout.ForceExpandHeight.Value = false;

        ui.Style.MinHeight = 52f;
        _promptTitle = ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.MiddleCenter);
        Fix(_promptTitle, TypeScale.Title);
        _promptTitle.Color.Value = Theme.Heading;

        ui.Style.MinHeight = 56f;
        _promptField = PromptField(ui, "prompt.listPlaceholder");
        _promptField.Editor.Target.LocalSubmitPressed += _ => SubmitPrompt();

        ui.Style.MinHeight = 52f;
        var row = ui.HorizontalLayout(10f, 0f, Alignment.MiddleCenter);
        var ok = ui.Button((LocaleString)UiStrings.Get("prompt.ok"), Theme.ButtonSelected);
        ok.LocalPressed += (_, _) => SubmitPrompt();
        var cancel = ui.Button((LocaleString)UiStrings.Get("prompt.cancel"), Theme.ButtonQuiet);
        cancel.LocalPressed += (_, _) => ClosePrompt();
        ui.NestOut();

        ui.NestOut();
        ui.NestOut();
        ui.NestOut();
        slot.ActiveSelf = false;
        _promptPanel = slot;
    }

    /// <summary>Asks for a name; <paramref name="onOk"/> gets the trimmed text when it is not blank.</summary>
    public void ShowPrompt(string title, string placeholder, string initial, Action<string> onOk)
    {
        _promptTitle.Content.Value = title;
        _promptField.TargetString = string.IsNullOrEmpty(initial) ? null : initial;
        _promptField.Slot.GetComponentInChildren<Text>().NullContent.Value = placeholder;
        _promptAction = onOk;
        _promptScrim.ActiveSelf = true;
        _promptPanel.ActiveSelf = true;
    }

    private void SubmitPrompt()
    {
        var text = (_promptField.TargetString ?? "").Trim();
        var action = _promptAction;
        ClosePrompt();
        if (text.Length > 0)
        {
            action?.Invoke(text);
        }
    }

    private void ClosePrompt()
    {
        _promptAction = null;
        _promptPanel.ActiveSelf = false;
        _promptScrim.ActiveSelf = false;
    }

    /// <summary>Plain text shown literally (verification V11).</summary>
    internal static string Plain(string value) => StringParsingHelper.SanitizeFormatTags(value) ?? "";

    internal string PlainText(string value) => Plain(value);

    // ------------------------------------------------------------------ binding

    private Text _cardsPerRowLabel = null!;
    private Text _hiddenRestoreLabel = null!;
    private Text _notifyLiveLabel = null!;
    private Text _notifyContactsLabel = null!;
    private Text _hiddenWordLabel = null!;

    /// <summary>Cards per row in the grid layout (0 = automatic); set from the settings.</summary>
    public int CardsPerRow { get; set; } = 4;

    private static readonly System.Reflection.FieldInfo? RectField =
        typeof(RectTransform).GetField("_rect", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    /// <summary>
    /// Sizes the grid cells so exactly <see cref="CardsPerRow"/> fit across the results area (the grid then stretches them
    /// to fill it). The area's width comes from the layout's computed rect, which has no public getter (read from the
    /// private <c>_rect</c> field, seen in a live dump), so this is skipped if that is ever missing.
    /// </summary>
    private void UpdateColumns()
    {
        if (_listLayout || RectField?.GetValue(_viewport) is not Rect rect)
        {
            return;
        }

        var columns = CardsPerRow > 0 ? CardsPerRow : 4;

        var width = rect.size.x;
        if (width < 200f)
        {
            return;
        }

        var spacing = Spacing.x;
        var cell = MathF.Max(120f, (width + spacing) / columns - spacing - 2f);
        var wanted = new float2(MathF.Round(cell), MathF.Round(cell * CellSize.y / CellSize.x));
        if (MathF.Abs(_grid.CellSize.Value.x - wanted.x) > 1f || MathF.Abs(_grid.CellSize.Value.y - wanted.y) > 1f)
        {
            _grid.CellSize.Value = wanted;
        }
    }

    private IReadOnlyList<CardModel> _pendingCards = [];
    private int _pendingShown;
    private int _pendingIndex;

    /// <summary>Cards filled in per call; set from the settings.</summary>
    public int CardBindsPerTick { get; set; } = 6;

    /// <summary>Card slots built per call; set from the settings.</summary>
    public int CardCreatesPerTick { get; set; } = 2;

    /// <summary>True while queued cards are still waiting to be filled in.</summary>
    public bool CardsPending => _pendingIndex < _pendingShown;

    /// <summary>Hides every card, so a lens switch does not show the old lens's cards while the new ones fill in.</summary>
    public void HideCards()
    {
        foreach (var card in _cards)
        {
            card.Hide();
        }
    }

    /// <summary>Builds and fills in the next few queued cards. Call once per tick.</summary>
    public void PumpCards()
    {
        UpdateColumns();
        for (var i = 0; i < _cards.Count; i++)
        {
            if (i >= _pendingIndex && i < _pendingShown)
            {
                continue; // still waiting to be refilled; its old picture must stay hidden
            }

            _cards[i].PollThumbnail();
        }

        var binds = 0;
        var creates = 0;
        PumpCardsCore(ref binds, ref creates);
    }

    private void PumpCardsCore(ref int binds, ref int creates)
    {
        while (_pendingIndex < _pendingShown && binds < CardBindsPerTick)
        {
            if (_pendingIndex >= _cards.Count)
            {
                if (creates >= CardCreatesPerTick)
                {
                    return;
                }

                var created = CardView.Create(_content, _screen.World, _rounded, _actions);
                created.SetListLayout(_listLayout);
                _cards.Add(created);
                creates++;
            }

            _cards[_pendingIndex].Bind(_pendingCards[_pendingIndex], _screen.Engine);
            _pendingIndex++;
            binds++;
        }
    }

    /// <summary>Binds a view: rail, header, bars, chips, menus, cards, empty state, banner, footer.</summary>
    public void Render(BrowseView view)
    {
        foreach (var (lens, (button, label)) in _rail)
        {
            var item = view.Rail.SelectMany(s => s.Items).FirstOrDefault(i => i.Lens == lens);
            if (item is null)
            {
                button.Slot.ActiveSelf = view.Rail.Count == 0; // left off by "live lenses only"; the paused view has no rail
                continue;
            }

            button.Slot.ActiveSelf = true;

            label.Content.Value = item.Label;
            var extras = _railExtras[lens];
            extras.Count.Content.Value = item.Count is null ? "" : item.Count.ToString()!;
            extras.Bar.ActiveSelf = item.Selected;
            SetColor(button, item.Selected ? Theme.Button : Theme.Rail);
        }

        _newListButton.Slot.ActiveSelf = view.ShowNewList || view.Rail.Count == 0;
        foreach (var (label, header) in _railHeaders)
        {
            header.ActiveSelf = view.Rail.Count == 0 || view.Rail.Any(s => s.Label == label);
        }

        var listItems = view.Rail.SelectMany(s => s.Items).Where(i => i.Lens == LensKind.NamedList).ToList();
        for (var i = 0; i < MaxListButtons; i++)
        {
            var (button, label) = _listButtons[i];
            if (i < listItems.Count)
            {
                var item = listItems[i];
                _listIds[i] = item.ListId;
                var name = Plain(CardText.Ellipsize(item.Label, 18));
                label.Content.Value = "<nobr>" + name + "</nobr>";
                _listExtras[i].Count.Content.Value = item.Count is null ? "" : item.Count.ToString()!;
                _listExtras[i].Bar.ActiveSelf = item.Selected;
                SetColor(button, item.Selected ? Theme.Button : Theme.Rail);
                button.Slot.ActiveSelf = true;
            }
            else
            {
                _listIds[i] = null;
                button.Slot.ActiveSelf = false;
            }
        }

        _listTools.ActiveSelf = view.ListTools;
        _title.Content.Value = "<nobr>" + Plain(view.Title) + "</nobr>";
        _subtitle.Content.Value = "<nobr>" + Plain(view.Subtitle) + "</nobr>";
        _count.Content.Value = view.Count;
        _sortButtonLabel.Content.Value = $"<nobr>{view.SortLabel} ▾</nobr>";
        _filtersButtonLabel.Content.Value = "<nobr>" + (view.FilterCount > 0 ? $"{UiStrings.Get("bar.filters")} ({view.FilterCount}) ▾" : UiStrings.Get("bar.filters") + " ▾") + "</nobr>";

        RenderChips(view.Chips);
        RenderSortMenu(view);
        RenderFiltersMenu(view);

        if (view.ListView != _listLayout)
        {
            _listLayout = view.ListView;
            _grid.CellSize.Value = _listLayout ? ListCellSize : CellSize;
            _grid.Spacing.Value = _listLayout ? ListSpacing : Spacing;
            foreach (var existing in _cards)
            {
                existing.SetListLayout(_listLayout);
            }
        }

        // Filling 40 cards in one frame (and building them the first time) stalls the game, so the cards are queued here
        // and PumpCards fills a few per tick.
        _pendingCards = view.Cards;
        _pendingShown = Math.Min(view.Cards.Count, MaxCards);
        _pendingIndex = 0;
        for (var i = _pendingShown; i < _cards.Count; i++)
        {
            _cards[i].Release();
        }

        // Cards still showing the previous lens's pictures while they wait to be refilled would flash them (in the wrong
        // row, once the layout reflows), so their pictures stay hidden until they are bound and loaded.
        for (var i = 0; i < Math.Min(_pendingShown, _cards.Count); i++)
        {
            if (_cards[i].Key != _pendingCards[i].Key)
            {
                _cards[i].HoldThumbnail();
            }
        }

        PumpCards();

        var empty = view.Cards.Count == 0 && view.EmptyMessage is not null;
        _emptyArea.ActiveSelf = empty;
        if (empty)
        {
            _emptyText.Content.Value = view.EmptyHint is null ? view.EmptyMessage! : $"{view.EmptyMessage}\n<size=60%>{view.EmptyHint}</size>";
            _clearFiltersButton.ActiveSelf = view.ShowClearFilters;
        }

        SetBanner(view.Banner);
        _status.Content.Value = view.Status;
        _previous.Slot.ActiveSelf = view.HasPrevious;
        _next.Slot.ActiveSelf = view.HasNext;
    }

    private void RenderChips(IReadOnlyList<ChipView> chips)
    {
        for (var i = 0; i < MaxChips; i++)
        {
            var (button, label) = _chips[i];
            if (i < chips.Count)
            {
                var chip = chips[i];
                _chipTags[i] = chip.Tag;
                var mark = chip.State switch { ChipState.Required => "+ ", ChipState.Excluded => "− ", _ => "" };
                label.Content.Value = mark + chip.Label;
                SetColor(button, chip.State == ChipState.Excluded ? Theme.ChipExcluded
                    : chip.Personal ? Theme.ChipPersonal
                    : chip.State == ChipState.Required ? Theme.ChipRequired
                    : Theme.Button);
                button.Slot.ActiveSelf = true;
            }
            else
            {
                _chipTags[i] = null;
                button.Slot.ActiveSelf = false;
            }
        }

        _clearChips.Slot.ActiveSelf = chips.Any(c => c.State is not null);
    }

    private void RenderSortMenu(BrowseView view)
    {
        foreach (var item in view.SortMenu)
        {
            var (button, label) = _sortItems[item.Key];
            label.Content.Value = "<nobr>" + (item.Selected ? "✓  " : "") + item.Label + "   <size=65%>" + item.Hint + "</size></nobr>";
            button.Enabled = item.Enabled;
            SetColor(button, item.Selected ? Theme.ButtonSelected : item.Enabled ? Theme.Button : Theme.ButtonQuiet);
        }

        SetColor(_descending, view.Descending ? Theme.ButtonSelected : Theme.Button);
        SetColor(_ascending, view.Descending ? Theme.Button : Theme.ButtonSelected);
    }

    private void RenderFiltersMenu(BrowseView view)
    {
        var f = view.Filters;
        Toggle(FilterOp.ToggleHasRoom, "filters.hasRoom", f.HasRoom, view.SessionLens);
        Toggle(FilterOp.ToggleMobile, "filters.mobile", f.MobileFriendly, view.SessionLens);
        Toggle(FilterOp.ToggleVr, "filters.vr", f.HasVr, view.SessionLens);
        Toggle(FilterOp.ToggleOtherVersions, "filters.otherVersions", view.ShowOtherVersions, true);
        Toggle(FilterOp.ToggleFeatured, "filters.featured", f.FeaturedOnly, !view.SessionLens);
        Toggle(FilterOp.ToggleIncludeEmpty, "filters.includeEmpty", f.IncludeEmpty, view.SessionLens);
        _filterItems[FilterOp.MinUptimeUp].Label.Content.Value = f.MinUptimeMinutes > 0
            ? UiStrings.Format("filters.minUptime", f.MinUptimeMinutes < 60 ? f.MinUptimeMinutes + " min" : f.MinUptimeMinutes / 60 + " h")
            : UiStrings.Get("filters.minUptime.any");
        _filterItems[FilterOp.MinUptimeUp].Button.Enabled = false;

        _filterItems[FilterOp.MinUsersUp].Label.Content.Value = f.MinUsers > 0 ? UiStrings.Format("filters.minUsers", f.MinUsers) : UiStrings.Get("filters.minUsers.any");
        _filterItems[FilterOp.MinVisitsUp].Label.Content.Value = f.MinVisits > 0 ? UiStrings.Format("filters.minVisits", CardText.Compact(f.MinVisits)) : UiStrings.Get("filters.minVisits.any");
        _filterItems[FilterOp.MinUsersUp].Button.Enabled = false;
        _filterItems[FilterOp.MinVisitsUp].Button.Enabled = false;

        foreach (var (op, host) in new[] { (FilterOp.HostAny, HostFilter.Any), (FilterOp.HostPerson, HostFilter.Person), (FilterOp.HostHeadless, HostFilter.Headless) })
        {
            SetColor(_filterItems[op].Button, f.Host == host ? Theme.ButtonSelected : Theme.Button);
            _filterItems[op].Button.Enabled = view.SessionLens;
        }
    }

    private void Toggle(FilterOp op, string stringId, bool on, bool enabled)
    {
        var (button, label) = _filterItems[op];
        label.Content.Value = $"<nobr>{UiStrings.Get(stringId)}   <size=65%>{UiStrings.Get(on ? "filters.on" : "filters.off")}</size></nobr>";
        SetColor(button, on ? Theme.ButtonSelected : enabled ? Theme.Button : Theme.ButtonQuiet);
        button.Enabled = enabled;
    }

    private void SetBanner(string? text)
    {
        var show = text is not null;
        var height = show ? 64f : 0f;
        _bannerRect.OffsetMin.Value = new float2(0f, -height);
        _bodyRect.OffsetMax.Value = new float2(0f, -height);
        _banner.ActiveSelf = show;
        if (show)
        {
            _bannerText.Content.Value = text!;
        }
    }

    /// <summary>The button's colour driver owns the visible tint; writing BaseColor alone is overridden.</summary>
    private static void SetColor(Button button, colorX color) => button.ColorDrivers[0].SetColors(color);

    public string SearchText
    {
        get => _search.TargetString ?? "";
        set
        {
            _search.TargetString = string.IsNullOrEmpty(value) ? null : value;
            _suggestMenu.ActiveSelf = false;
        }
    }

    // ------------------------------------------------------------------ transitions

    // Tween<T> also calls its done-callback when it is destroyed early, and TweenTo clears (destroys) the field's
    // previous tween. Each animation therefore takes a generation number, and a callback acts only if it is still
    // the newest one; otherwise a second click mid-transition would run the old swap or hide the new curtain.

    /// <summary>Fades the curtain in over the main pane, then calls <paramref name="onCovered"/> (update thread).</summary>
    public void Cover(float seconds, Action onCovered)
    {
        var generation = ++_curtainGeneration;
        _curtain.Slot.ActiveSelf = true;
        _curtain.Tint.TweenTo(Theme.CurtainShown, seconds, CurvePreset.SineIn, onDoneLocal: () =>
        {
            if (generation == _curtainGeneration)
            {
                onCovered();
            }
        });
    }

    /// <summary>Fades the curtain out while the results rise <paramref name="rise"/> units into place.</summary>
    public void Reveal(float seconds, float rise)
    {
        var generation = ++_curtainGeneration;
        _curtain.Slot.ActiveSelf = true;
        _curtain.Tint.TweenFromTo(Theme.CurtainShown, Theme.CurtainHidden, seconds, CurvePreset.CubicOut, onDoneLocal: () =>
        {
            if (generation == _curtainGeneration)
            {
                _curtain.Slot.ActiveSelf = false;
            }
        });

        if (rise > 0f)
        {
            var down = new float2(0f, rise);
            _viewport.OffsetMin.TweenFromTo(_viewportMin - down, _viewportMin, seconds, CurvePreset.CubicOut);
            _viewport.OffsetMax.TweenFromTo(_viewportMax - down, _viewportMax, seconds, CurvePreset.CubicOut);
        }
    }

    /// <summary>Ends any transition at once: curtain hidden, results in place.</summary>
    public void Settle()
    {
        _curtainGeneration++;
        _curtain.Tint.ClearTweens();
        _viewport.OffsetMin.ClearTweens();
        _viewport.OffsetMax.ClearTweens();
        _curtain.Tint.Value = Theme.CurtainHidden;
        _curtain.Slot.ActiveSelf = false;
        _viewport.OffsetMin.Value = _viewportMin;
        _viewport.OffsetMax.Value = _viewportMax;
    }

    // ------------------------------------------------------------------ helpers

    internal static Text TextIn(UIBuilder ui, RectTransform rect, Alignment alignment, colorX color, float min, float max)
    {
        ui.NestInto(rect);
        var text = ui.Text((LocaleString)"", bestFit: true, alignment: alignment);
        Clamp(text, min, max);
        text.Color.Value = color;
        ui.NestOut();
        return text;
    }

    /// <summary>A narrow auto-size range keeps the type steady from card to card; long text is cut by the caller.</summary>
    internal static void Clamp(Text text, float min, float max)
    {
        text.AutoSizeMin.Value = min;
        text.AutoSizeMax.Value = max;
    }

    /// <summary>One fixed size from <see cref="TypeScale"/>: no shrinking to fit, so equal kinds of text look equal.</summary>
    internal static void Fix(Text text, float size)
    {
        text.Size.Value = size;
        text.AutoSizeMin.Value = size;
        text.AutoSizeMax.Value = size;
    }
}

/// <summary>
/// One pooled card (spec "Card anatomy"): thumbnail with a users pill and a status pill; title; host and age; up to
/// three tag buttons (a tag adds a required chip, F5); visits and the action button. All content is a
/// <see cref="CardModel"/>, so this class only draws.
/// </summary>
internal sealed class CardView
{
    private readonly Slot _root;
    private readonly IBrowseActions _actions;
    private Text _title = null!;
    private Text _meta = null!;
    private Text _users = null!;
    private Text _statusPillText = null!;
    private Text _footer = null!;
    private Slot _usersPill = null!;
    private Slot _statusPill = null!;
    private readonly List<(Button Button, Text Label)> _tags = [];
    private readonly bool[] _tagPersonal = new bool[CardModel.MaxTags];
    private Button _star = null!;
    private Text _starLabel = null!;
    private Button _organize = null!;
    private Button _info = null!;
    private Slot _body = null!;
    private RectTransform[] _parts = [];
    private (float2 Min, float2 Max)[] _gridAnchors = [];
    private (float2 Min, float2 Max)[] _gridOverlayAnchors = [];
    private Button _button = null!;
    private Text _buttonLabel = null!;
    private StaticTexture2D _thumbnail = null!;
    private Component _thumbGraphic = null!;
    private DateTime _thumbWaitUntilUtc = DateTime.MinValue;
    private Uri? _thumbnailUri;
    private Slot _scrim = null!;
    private Slot _catcher = null!;
    private bool _list;

    private CardView(Slot root, IBrowseActions actions)
    {
        _root = root;
        _actions = actions;
    }

    public string? Key { get; private set; }

    public static CardView Create(Slot parent, World world, IAssetProvider<Sprite> rounded, IBrowseActions actions)
    {
        var slot = parent.AddSlot("Card");
        slot.PersistentSelf = false;
        var card = new CardView(slot, actions);

        var ui = new UIBuilder(slot.AttachComponent<RectTransform>());
        RadiantUI_Constants.SetupDefaultStyle(ui);
        ui.Style.ButtonColor = Theme.Button;
        ui.Panel(Theme.Card, rounded, NineSliceSizing.FixedSize); // rounded card; Panel nests

        var body = ui.Next("Body");
        body.GetComponent<RectTransform>().AddFixedPadding(8f);
        ui.Nest();

        // thumbnail, gap, title, host and age, tags, footer with the action button
        var parts = ui.SplitVertically(0.43f, 0.02f, 0.12f, 0.09f, 0.13f, 0.21f);
        card._body = body;
        card._parts = parts.ToArray();
        card._gridAnchors = parts.Select(p => (p.AnchorMin.Value, p.AnchorMax.Value)).ToArray();

        // Thumbnail: world thumbnails are 360 degree images, drawn the way the stock card draws them (verification V10).
        var texture = parts[0].Slot.AttachComponent<StaticTexture2D>();
        var block = parts[0].Slot.AttachComponent<Projection360PropertyBlock>();
        block.Texture.Target = texture;
        block.PerspectiveFieldOfView.Value = float2.One * 110f;
        ui.NestInto(parts[0]);
        card._thumbGraphic = ui.RawGraphic(LegacyWorldThumbnailItem.GetWorldPreviewMaterial(world), block);
        card._thumbnail = texture;

        // Grid cards are the picture with the name, author, users and friends over it (a darkened strip keeps the text
        // legible); everything else is in the detail panel, which a press anywhere on the card opens.
        var scrim = ui.Image(colorX.Black.SetA(0.55f));
        scrim.Slot.Name = "Scrim";
        scrim.RectTransform.AnchorMax.Value = new float2(1f, 0.36f);
        card._scrim = scrim.Slot;
        card._usersPill = Pill(ui, rounded, new float2(0.03f, 0.06f), new float2(0.34f, 0.26f), out card._users);
        card._statusPill = Pill(ui, rounded, new float2(0.03f, 0.74f), new float2(0.46f, 0.94f), out card._statusPillText);

        // Favorite star (top right) and the lists button (bottom right), laid over the thumbnail. Opaque on purpose: a
        // translucent Button turns opaque grey when hovered (see Catcher).
        card._star = OverlayButton(ui, "★", new float2(0.82f, 0.70f), new float2(0.97f, 0.96f), out card._starLabel);
        DiscoverUi.Clamp(card._starLabel, 16f, 28f);
        card._star.LocalPressed += (_, _) =>
        {
            if (card.Key is not null)
            {
                actions.ToggleFavorite(card.Key);
            }
        };
        var info = OverlayButton(ui, UiStrings.Get("card.info"), new float2(0.50f, 0.74f), new float2(0.78f, 0.96f), out var infoLabel);
        card._info = info;
        DiscoverUi.Clamp(infoLabel, 12f, 18f);
        info.LocalPressed += (_, _) =>
        {
            if (card.Key is not null)
            {
                actions.OpenDetails(card.Key);
            }
        };
        card._organize = OverlayButton(ui, UiStrings.Get("card.organize"), new float2(0.66f, 0.04f), new float2(0.97f, 0.28f), out var organizeLabel);
        DiscoverUi.Clamp(organizeLabel, 12f, 18f);
        card._gridOverlayAnchors =
        [
            (card._star.Slot.GetComponent<RectTransform>().AnchorMin.Value, card._star.Slot.GetComponent<RectTransform>().AnchorMax.Value),
            (card._info.Slot.GetComponent<RectTransform>().AnchorMin.Value, card._info.Slot.GetComponent<RectTransform>().AnchorMax.Value),
            (card._organize.Slot.GetComponent<RectTransform>().AnchorMin.Value, card._organize.Slot.GetComponent<RectTransform>().AnchorMax.Value),
        ];
        card._organize.LocalPressed += (_, _) =>
        {
            if (card.Key is not null)
            {
                actions.OpenOrganize(card.Key);
            }
        };
        ui.NestOut();

        card._title = DiscoverUi.TextIn(ui, parts[2], Alignment.MiddleLeft, Theme.Text, 18f, 26f);
        card._meta = DiscoverUi.TextIn(ui, parts[3], Alignment.MiddleLeft, Theme.TextSecondary, 14f, 18f);

        ui.NestInto(parts[4]);
        var tagRow = ui.HorizontalLayout(6f, 0f, Alignment.MiddleLeft);
        tagRow.ForceExpandWidth.Value = false;
        ui.Style.MinHeight = 0f;
        ui.Style.MinWidth = 78f;
        for (var i = 0; i < CardModel.MaxTags; i++)
        {
            var tag = ui.Button((LocaleString)"");
            var label = tag.Slot.GetComponentInChildren<Text>();
            DiscoverUi.Clamp(label, 12f, 16f);
            var index = i;
            tag.LocalPressed += (_, _) =>
            {
                if (card._tags[index].Label.Content.Value is { Length: > 0 } text)
                {
                    actions.CardTagPressed(text, card._tagPersonal[index]);
                }
            };
            card._tags.Add((tag, label));
        }

        ui.Style.MinWidth = 0f;
        ui.NestOut();
        ui.NestOut();

        ui.NestInto(parts[5]);
        ui.SplitHorizontally(0.58f, out var footerRect, out var buttonRect, 0.02f);
        card._footer = DiscoverUi.TextIn(ui, footerRect, Alignment.MiddleLeft, Theme.TextSecondary, 13f, 17f);
        buttonRect.AddFixedPadding(6f); // keeps the button smaller than its row, like the M2 card's
        ui.NestInto(buttonRect);
        card._button = ui.Button((LocaleString)UiStrings.Get("card.join"), Theme.ButtonSelected);
        card._buttonLabel = card._button.Slot.GetComponentInChildren<Text>();
        DiscoverUi.Clamp(card._buttonLabel, 13f, 17f);
        card._button.LocalPressed += (_, _) =>
        {
            if (card.Key is not null)
            {
                actions.CardPressed(card.Key);
            }
        };
        ui.NestOut();
        ui.NestOut();
        ui.NestOut();

        // The press target over the whole card (grid mode). Direct colours, all the same: no hover tint and no opaque grey (see Catcher).
        var catcher = ui.Image(colorX.Black.SetA(0.02f));
        catcher.Slot.Name = "Open";
        var press = catcher.Slot.AttachComponent<Button>();
        var driver = press.ColorDrivers[0];
        driver.TintColorMode.Value = InteractionElement.ColorMode.Direct;
        driver.NormalColor.Value = colorX.Black.SetA(0.02f);
        driver.HighlightColor.Value = colorX.Black.SetA(0.02f); // no hover tint: it hides the preview
        driver.PressColor.Value = colorX.Black.SetA(0.02f);
        driver.DisabledColor.Value = colorX.Black.SetA(0.02f);
        press.LocalPressed += (_, _) =>
        {
            if (card.Key is not null)
            {
                actions.OpenDetails(card.Key);
            }
        };
        card._catcher = catcher.Slot;

        // Hovering pans the 360 preview, exactly as on the stock card: its panner reads hover events from this button.
        var panner = catcher.Slot.AttachComponent<Perspective360Panner>();
        block.PerspectiveAngleOffset.DriveFrom(panner.AngleOffset);
        block.PerspectiveFieldOfView.DriveFrom(panner.FOV);
        card.SetListLayout(false);
        return card;
    }

    // Grid layout: the thumbnail fills the card and the title and host sit on its darkened lower strip. The gap, tag row
    // and footer keep their old places but are hidden.
    private static readonly (float2 Min, float2 Max)[] GridAnchors =
    [
        (new float2(0.00f, 0.00f), new float2(1.00f, 1.00f)),
        (new float2(0.00f, 0.55f), new float2(1.00f, 0.57f)),
        (new float2(0.03f, 0.15f), new float2(0.97f, 0.31f)),
        (new float2(0.03f, 0.03f), new float2(0.97f, 0.15f)),
        (new float2(0.00f, 0.21f), new float2(1.00f, 0.34f)),
        (new float2(0.00f, 0.00f), new float2(1.00f, 0.21f)),
    ];

    // Users and status pills, by mode (fractions of the thumbnail's rect).
    private static readonly (float2 Min, float2 Max) GridUsers = (new float2(0.03f, 0.80f), new float2(0.28f, 0.96f));
    private static readonly (float2 Min, float2 Max) GridFriends = (new float2(0.30f, 0.80f), new float2(0.72f, 0.96f));
    private static readonly (float2 Min, float2 Max) ListUsers = (new float2(0.03f, 0.06f), new float2(0.34f, 0.26f));
    private static readonly (float2 Min, float2 Max) ListStatus = (new float2(0.03f, 0.74f), new float2(0.46f, 0.94f));

    private static void Place(Slot slot, (float2 Min, float2 Max) anchors)
    {
        var rect = slot.GetComponent<RectTransform>();
        rect.AnchorMin.Value = anchors.Min;
        rect.AnchorMax.Value = anchors.Max;
    }

    // List layout: thumbnail on the left, title, host and tags in the middle, the buttons and the action on the right.
    private static readonly (float2 Min, float2 Max)[] ListAnchors =
    [
        (new float2(0.00f, 0.00f), new float2(0.17f, 1.00f)),
        (new float2(0.17f, 0.00f), new float2(0.19f, 1.00f)),
        (new float2(0.19f, 0.55f), new float2(0.61f, 1.00f)),
        (new float2(0.19f, 0.30f), new float2(0.61f, 0.55f)),
        (new float2(0.19f, 0.00f), new float2(0.61f, 0.30f)),
        (new float2(0.62f, 0.00f), new float2(1.00f, 0.48f)),
    ];

    private static readonly (float2 Min, float2 Max)[] ListOverlayAnchors =
    [
        (new float2(0.91f, 0.54f), new float2(1.00f, 1.00f)), // star
        (new float2(0.62f, 0.54f), new float2(0.76f, 1.00f)), // info
        (new float2(0.77f, 0.54f), new float2(0.90f, 1.00f)), // lists
    ];

    /// <summary>Re-anchors the pooled card as a row (true) or a grid card (false). The overlay buttons move out of the thumbnail in a row.</summary>
    public void SetListLayout(bool list)
    {
        _list = list;
        _scrim.ActiveSelf = !list;
        _catcher.ActiveSelf = !list;
        _parts[1].Slot.ActiveSelf = list;
        _parts[4].Slot.ActiveSelf = list;
        _parts[5].Slot.ActiveSelf = list;
        Place(_usersPill, list ? ListUsers : GridUsers);
        Place(_statusPill, list ? ListStatus : GridFriends);
        _info.Slot.ActiveSelf = list;
        for (var i = 0; i < _parts.Length; i++)
        {
            var (min, max) = list ? ListAnchors[i] : GridAnchors[i];
            _parts[i].AnchorMin.Value = min;
            _parts[i].AnchorMax.Value = max;
        }

        var thumbnail = _parts[0].Slot;
        var buttons = new[] { _star, _info, _organize };
        for (var i = 0; i < buttons.Length; i++)
        {
            var slot = buttons[i].Slot;
            slot.SetParent(list ? _body : thumbnail, false);
            var rect = slot.GetComponent<RectTransform>();
            var (min, max) = list ? ListOverlayAnchors[i] : _gridOverlayAnchors[i];
            rect.AnchorMin.Value = min;
            rect.AnchorMax.Value = max;
        }
    }

    private static Button OverlayButton(UIBuilder ui, string text, float2 anchorMin, float2 anchorMax, out Text label)
    {
        var button = ui.Button((LocaleString)text, Theme.Button);
        var rect = button.Slot.GetComponent<RectTransform>();
        rect.AnchorMin.Value = anchorMin;
        rect.AnchorMax.Value = anchorMax;
        label = button.Slot.GetComponentInChildren<Text>();
        return button;
    }

    /// <summary>A small rounded label laid over the thumbnail, placed by fractions of the thumbnail's rect.</summary>
    private static Slot Pill(UIBuilder ui, IAssetProvider<Sprite> sprite, float2 anchorMin, float2 anchorMax, out Text text)
    {
        var image = ui.Panel(Theme.Pill, sprite, NineSliceSizing.FixedSize);
        image.RectTransform.AnchorMin.Value = anchorMin;
        image.RectTransform.AnchorMax.Value = anchorMax;
        text = ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.MiddleCenter);
        DiscoverUi.Clamp(text, 12f, 18f);
        text.Color.Value = Theme.Text;
        text.RectTransform.AddFixedPadding(4f);
        ui.NestOut();
        return image.Slot;
    }

    public void Bind(CardModel card, Engine engine)
    {
        Key = card.Key;
        _root.ActiveSelf = true;

        _title.Content.Value = Safe(card.Title);
        _title.Color.Value = card.Dimmed ? Theme.TextSecondary : Theme.Text;
        _meta.Content.Value = "<nobr>" + Safe(card.Meta) + "</nobr>";

        _usersPill.ActiveSelf = card.Users is not null;
        _users.Content.Value = card.Users ?? "";

        var friends = card.Badges.FirstOrDefault(b => b.Kind == BadgeKind.Friends)?.Text;
        var status = !_list ? (friends is null ? null : "★ " + friends)
            : card.IsOpen ? UiStrings.Get("card.open") : card.Featured ? "★ " + UiStrings.Get("card.featured") : null;
        _statusPill.ActiveSelf = status is not null;
        _statusPillText.Content.Value = status ?? "";

        for (var i = 0; i < _tags.Count; i++)
        {
            var show = i < card.Tags.Count;
            _tags[i].Button.Slot.ActiveSelf = show;
            _tags[i].Label.Content.Value = show ? card.Tags[i].Text : "";
            _tagPersonal[i] = show && card.Tags[i].Personal;
            if (show)
            {
                _tags[i].Button.ColorDrivers[0].SetColors(_tagPersonal[i] ? Theme.ChipPersonal : Theme.Button);
            }
        }

        var isWorld = Discover.Core.Model.DiscoveryKeys.IsSavable(card.Key);
        _star.Slot.ActiveSelf = isWorld && _list;
        _organize.Slot.ActiveSelf = isWorld && _list;
        _starLabel.Color.Value = card.IsFavorite ? Theme.Favorite : Theme.TextSecondary;

        _footer.Content.Value = BadgeText(card);

        _buttonLabel.Content.Value = UiStrings.Get(card.Action switch
        {
            CardAction.GoThere => "card.goThere",
            CardAction.NewSession => "card.newSession",
            CardAction.Full => "card.full",
            CardAction.Offline => "card.offline",
            CardAction.OtherVersion => "card.otherVersion",
            _ => "card.join",
        });
        var enabled = card.Action is CardAction.Join or CardAction.GoThere or CardAction.NewSession;
        _button.Enabled = enabled;
        _button.ColorDrivers[0].SetColors(!enabled ? Theme.ButtonQuiet : card.Action == CardAction.GoThere ? Theme.Button : Theme.ButtonSelected);

        SetThumbnail(card.ThumbnailUrl, engine);
    }

    /// <summary>Badges (coloured) then visits, on the footer line; colour is never the only signal (each badge is text).</summary>
    private static string BadgeText(CardModel card)
    {
        var parts = new List<string>(4);
        foreach (var badge in card.Badges)
        {
            var hex = badge.Kind switch
            {
                BadgeKind.Friends or BadgeKind.Here => RadiantUI_Constants.Hero.GREEN_HEX,
                BadgeKind.Mobile => RadiantUI_Constants.Hero.CYAN_HEX,
                BadgeKind.Full => RadiantUI_Constants.Hero.RED_HEX,
                _ => RadiantUI_Constants.Neutrals.MIDLIGHT_HEX,
            };
            parts.Add($"<color={hex}>{(badge.Kind == BadgeKind.Friends ? "★ " : "")}{badge.Text}</color>");
        }

        if (card.Footer.Length > 0)
        {
            parts.Add(card.Footer);
        }

        return "<nobr>" + string.Join("  ", parts) + "</nobr>";
    }

    /// <summary>Hides the card but keeps its thumbnail loaded; a later <see cref="Bind"/> shows it again.</summary>
    public void Hide() => _root.ActiveSelf = false;

    /// <summary>Hides the card and unloads its thumbnail.</summary>
    public void Release()
    {
        Key = null;
        _root.ActiveSelf = false;
        if (_thumbnailUri is not null)
        {
            _thumbnail.URL.Value = null!;
            _thumbnailUri = null;
        }
    }

    private void SetThumbnail(string? raw, Engine engine)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            uri = OfficialAssets.Skyboxes.Thumbnails.NoThumbnail;
        }

        if (uri == _thumbnailUri)
        {
            return;
        }

        var direct = uri.Scheme != engine.Cloud.Assets.DBScheme;
        _thumbnail.Uncompressed.Value = direct;
        _thumbnail.DirectLoad.Value = direct;
        _thumbnail.URL.Value = uri;
        _thumbnailUri = uri;

        // A recycled card would otherwise show a blank (flashing) picture until the new texture arrives, so the picture
        // stays hidden until it is available (PollThumbnail), or for at most a few seconds.
        _thumbGraphic.Enabled = false;
        _thumbWaitUntilUtc = DateTime.UtcNow + TimeSpan.FromSeconds(4);
    }

    /// <summary>Hides the picture until <see cref="PollThumbnail"/> finds its texture ready.</summary>
    public void HoldThumbnail()
    {
        _thumbGraphic.Enabled = false;
        _thumbWaitUntilUtc = DateTime.UtcNow + TimeSpan.FromSeconds(4);
    }

    /// <summary>Shows the picture once its texture has loaded. Cheap; called every tick.</summary>
    public void PollThumbnail()
    {
        if (_thumbWaitUntilUtc == DateTime.MinValue)
        {
            return;
        }

        if (_thumbnail.IsAssetAvailable || DateTime.UtcNow >= _thumbWaitUntilUtc)
        {
            _thumbGraphic.Enabled = true;
            _thumbWaitUntilUtc = DateTime.MinValue;
        }
    }

    /// <summary>Plain text shown literally (verification V11).</summary>
    private static string Safe(string value) => StringParsingHelper.SanitizeFormatTags(value) ?? "";
}
