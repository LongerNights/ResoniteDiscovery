using Discover.Core.Lenses;
using Discover.Core.Presentation;
using Discover.Core.Search;
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
    private const int MaxChips = 10;
    private static readonly float2 CellSize = new(360f, 300f);
    private static readonly float2 Spacing = new(16f, 16f);

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
        Clamp(title, 28f, 44f);
        title.Color.Value = Theme.Heading;

        foreach (var (label, lenses) in new[]
        {
            (UiStrings.Get("rail.live"), new[] { LensKind.LiveNow, LensKind.Friends }),
            (UiStrings.Get("rail.browse"), new[] { LensKind.New, LensKind.MostVisited, LensKind.Featured, LensKind.Random }),
        })
        {
            ui.Style.MinHeight = 34f;
            var section = ui.Text((LocaleString)label, bestFit: true, alignment: Alignment.BottomLeft);
            Clamp(section, 14f, 20f);
            section.Color.Value = Theme.TextSecondary;

            ui.Style.MinHeight = 46f;
            foreach (var lens in lenses)
            {
                AddRailButton(ui, lens);
            }
        }

        // MINE: Favorites, the named lists (a small pool), "+ New list", Recent.
        ui.Style.MinHeight = 34f;
        var mine = ui.Text((LocaleString)UiStrings.Get("rail.mine"), bestFit: true, alignment: Alignment.BottomLeft);
        Clamp(mine, 14f, 20f);
        mine.Color.Value = Theme.TextSecondary;

        ui.Style.MinHeight = 46f;
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
            button.Slot.ActiveSelf = false;
            _listButtons.Add((button, label));
        }

        var newList = ui.Button((LocaleString)UiStrings.Get("rail.newList"), Theme.ButtonQuiet);
        newList.LocalPressed += (_, _) => _actions.NewListPrompt();
        AddRailButton(ui, LensKind.Recent);

        ui.NestOut();
        ui.NestOut();
        ui.NestOut();
    }

    private void AddRailButton(UIBuilder ui, LensKind lens)
    {
        var button = ui.Button((LocaleString)RailBuilder.Title(lens));
        var text = button.Slot.GetComponentInChildren<Text>();
        button.LocalPressed += (_, _) => _actions.SelectLens(lens);
        _rail[lens] = (button, text);
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
        var parts = ui.SplitHorizontally(0.46f, 0.20f, 0.19f, 0.15f);
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
        sort.LocalPressed += (_, _) => ToggleMenu(_sortMenu);
        ui.NestOut();

        ui.NestInto(parts[2]);
        var filters = ui.Button((LocaleString)"");
        _filtersButtonLabel = filters.Slot.GetComponentInChildren<Text>();
        filters.LocalPressed += (_, _) => ToggleMenu(_filtersMenu);
        ui.NestOut();

        ui.NestInto(parts[3]);
        var refresh = ui.Button((LocaleString)UiStrings.Get("bar.refresh"));
        refresh.LocalPressed += (_, _) => _actions.Refresh();
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
        ui.Style.MinWidth = 120f;
        for (var i = 0; i < MaxChips; i++)
        {
            var chip = ui.Button((LocaleString)"");
            var label = chip.Slot.GetComponentInChildren<Text>();
            Clamp(label, 14f, 22f);
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

    private void BuildHeader(UIBuilder ui, RectTransform header)
    {
        header.AddFixedPadding(6f);
        ui.NestInto(header);
        var parts = ui.SplitHorizontally(0.25f, 0.28f, 0.17f, 0.30f);
        _title = TextIn(ui, parts[0], Alignment.MiddleLeft, Theme.Heading, 26f, 44f);
        _subtitle = TextIn(ui, parts[1], Alignment.MiddleLeft, Theme.TextSecondary, 16f, 22f);
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
            Clamp(button.Slot.GetComponentInChildren<Text>(), 11f, 15f);
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
        var parts = ui.SplitHorizontally(0.60f, 0.20f, 0.20f);
        _status = TextIn(ui, parts[0], Alignment.MiddleLeft, Theme.TextSecondary, 14f, 20f);
        parts[1].AddFixedPadding(4f);
        parts[2].AddFixedPadding(4f);

        ui.NestInto(parts[1]);
        _previous = ui.Button((LocaleString)UiStrings.Get("nav.previous"));
        _previous.LocalPressed += (_, _) => _actions.PreviousPage();
        ui.NestOut();

        ui.NestInto(parts[2]);
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

        _sortMenu = BuildMenu(ui, new float2(0.42f, 0.26f), new float2(0.72f, 0.90f), BuildSortMenu);
        _filtersMenu = BuildMenu(ui, new float2(0.54f, 0.06f), new float2(0.99f, 0.90f), BuildFiltersMenu);
        _suggestMenu = BuildMenu(ui, new float2(0.02f, 0.58f), new float2(0.40f, 0.90f), BuildSuggestMenu);
        BuildConfirm(ui);
        BuildOrganize(ui);
        BuildPrompt(ui);
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

        var slot = ui.Empty("Confirm");
        var rect = slot.GetComponent<RectTransform>();
        rect.AnchorMin.Value = new float2(0.26f, 0.22f);
        rect.AnchorMax.Value = new float2(0.74f, 0.80f);
        ui.NestInto(rect);
        ui.Panel(Theme.Rail, _rounded, NineSliceSizing.FixedSize);
        var layout = ui.VerticalLayout(10f, 22f, Alignment.TopCenter);
        layout.ForceExpandHeight.Value = false;

        ui.Style.MinHeight = 58f;
        _confirmTitle = ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.MiddleCenter);
        Clamp(_confirmTitle, 24f, 36f);
        _confirmTitle.Color.Value = Theme.Heading;

        ui.Style.MinHeight = 84f;
        _confirmBody = ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.MiddleCenter);
        Clamp(_confirmBody, 18f, 26f);
        _confirmBody.Color.Value = Theme.Text;

        ui.Style.MinHeight = 54f;
        for (var i = 0; i < MaxConfirmChoices; i++)
        {
            var button = ui.Button((LocaleString)"", Theme.ButtonSelected);
            var label = button.Slot.GetComponentInChildren<Text>();
            Clamp(label, 16f, 24f);
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
        Clamp(cancel.Slot.GetComponentInChildren<Text>(), 16f, 24f);
        cancel.LocalPressed += (_, _) => CloseMenus();

        ui.NestOut();
        ui.NestOut();
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

    private Slot BuildMenu(UIBuilder ui, float2 anchorMin, float2 anchorMax, Action<UIBuilder> fill)
    {
        var slot = ui.Empty("Menu");
        var rect = slot.GetComponent<RectTransform>();
        rect.AnchorMin.Value = anchorMin;
        rect.AnchorMax.Value = anchorMax;
        ui.NestInto(rect);
        ui.Panel(Theme.Rail, _rounded, NineSliceSizing.FixedSize);
        var layout = ui.VerticalLayout(6f, 14f, Alignment.TopLeft);
        layout.ForceExpandHeight.Value = false;
        ui.Style.MinHeight = 46f;
        fill(ui);
        ui.NestOut();
        ui.NestOut();
        ui.NestOut();
        slot.ActiveSelf = false;
        return slot;
    }

    private void BuildSortMenu(UIBuilder ui)
    {
        var title = ui.Text((LocaleString)UiStrings.Get("sort.title"), bestFit: true, alignment: Alignment.MiddleLeft);
        Clamp(title, 14f, 20f);
        title.Color.Value = Theme.TextSecondary;

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
        Clamp(title, 20f, 30f);
        title.Color.Value = Theme.Heading;
        var reset = ui.Button((LocaleString)UiStrings.Get("filters.reset"), Theme.ButtonQuiet);
        reset.LocalPressed += (_, _) => _actions.Filter(FilterOp.Reset);
        ui.NestOut();

        Stepper(ui, FilterOp.MinUsersDown, FilterOp.MinUsersUp);
        Stepper(ui, FilterOp.MinVisitsDown, FilterOp.MinVisitsUp);
        foreach (var op in new[] { FilterOp.ToggleHasRoom, FilterOp.ToggleMobile, FilterOp.ToggleVr, FilterOp.ToggleOtherVersions })
        {
            var button = ui.Button((LocaleString)"");
            var captured = op;
            button.LocalPressed += (_, _) => _actions.Filter(captured);
            _filterItems[op] = (button, button.Slot.GetComponentInChildren<Text>());
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

    private void ToggleMenu(Slot menu)
    {
        var open = !menu.ActiveSelf;
        CloseMenus();
        menu.ActiveSelf = open;
        _scrim.ActiveSelf = open;
    }

    public void CloseMenus()
    {
        _sortMenu.ActiveSelf = false;
        _filtersMenu.ActiveSelf = false;
        _suggestMenu.ActiveSelf = false;
        _scrim.ActiveSelf = false;
        _confirmPanel.ActiveSelf = false;
        _confirmScrim.ActiveSelf = false;
        _organizePanel.ActiveSelf = false;
        _organizeScrim.ActiveSelf = false;
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
        ui.Panel(Theme.Rail, _rounded, NineSliceSizing.FixedSize);
        var layout = ui.VerticalLayout(8f, 20f, Alignment.TopCenter);
        layout.ForceExpandHeight.Value = false;

        ui.Style.MinHeight = 50f;
        _organizeTitle = ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.MiddleCenter);
        Clamp(_organizeTitle, 22f, 32f);
        _organizeTitle.Color.Value = Theme.Heading;

        ui.Style.MinHeight = 28f;
        var lists = ui.Text((LocaleString)UiStrings.Get("organize.lists"), bestFit: true, alignment: Alignment.BottomLeft);
        Clamp(lists, 13f, 18f);
        lists.Color.Value = Theme.TextSecondary;

        ui.Style.MinHeight = 40f;
        _organizeNoLists = ui.Text((LocaleString)UiStrings.Get("organize.noLists"), bestFit: true, alignment: Alignment.MiddleLeft);
        Clamp(_organizeNoLists, 14f, 20f);
        _organizeNoLists.Color.Value = Theme.TextSecondary;

        ui.Style.MinHeight = 42f;
        for (var i = 0; i < MaxOrganizeLists; i++)
        {
            var button = ui.Button((LocaleString)"");
            var label = button.Slot.GetComponentInChildren<Text>();
            Clamp(label, 14f, 20f);
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
        Clamp(newList.Slot.GetComponentInChildren<Text>(), 14f, 20f);
        newList.LocalPressed += (_, _) => _actions.OrganizeNewList();

        ui.Style.MinHeight = 28f;
        var tags = ui.Text((LocaleString)UiStrings.Get("organize.tags"), bestFit: true, alignment: Alignment.BottomLeft);
        Clamp(tags, 13f, 18f);
        tags.Color.Value = Theme.TextSecondary;

        ui.Style.MinHeight = 38f;
        _organizeNoTags = ui.Text((LocaleString)UiStrings.Get("organize.noTags"), bestFit: true, alignment: Alignment.MiddleLeft);
        Clamp(_organizeNoTags, 14f, 20f);
        _organizeNoTags.Color.Value = Theme.TextSecondary;

        ui.Style.MinHeight = 40f;
        var tagRow = ui.HorizontalLayout(6f, 0f, Alignment.MiddleLeft);
        tagRow.ForceExpandWidth.Value = false;
        ui.Style.MinWidth = 96f;
        for (var i = 0; i < MaxOrganizeTags; i++)
        {
            var button = ui.Button((LocaleString)"", Theme.ChipPersonal);
            var label = button.Slot.GetComponentInChildren<Text>();
            Clamp(label, 12f, 17f);
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
        Clamp(suggested, 13f, 18f);
        suggested.Color.Value = Theme.TextSecondary;

        ui.Style.MinHeight = 40f;
        var suggestRow = ui.HorizontalLayout(6f, 0f, Alignment.MiddleLeft);
        suggestRow.ForceExpandWidth.Value = false;
        ui.Style.MinWidth = 96f;
        for (var i = 0; i < MaxTagSuggestions; i++)
        {
            var button = ui.Button((LocaleString)"", Theme.Button);
            var label = button.Slot.GetComponentInChildren<Text>();
            Clamp(label, 12f, 17f);
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
            Clamp(label, 14f, 20f);
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
        ui.Panel(Theme.Rail, _rounded, NineSliceSizing.FixedSize);
        var layout = ui.VerticalLayout(12f, 22f, Alignment.TopCenter);
        layout.ForceExpandHeight.Value = false;

        ui.Style.MinHeight = 52f;
        _promptTitle = ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.MiddleCenter);
        Clamp(_promptTitle, 22f, 32f);
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

    // ------------------------------------------------------------------ binding

    /// <summary>Binds a view: rail, header, bars, chips, menus, cards, empty state, banner, footer.</summary>
    public void Render(BrowseView view)
    {
        foreach (var (lens, (button, label)) in _rail)
        {
            var item = view.Rail.SelectMany(s => s.Items).FirstOrDefault(i => i.Lens == lens);
            if (item is null)
            {
                continue; // the paused view has no rail
            }

            label.Content.Value = item.Count is null ? item.Label : $"{item.Label}   {item.Count}";
            SetColor(button, item.Selected ? Theme.ButtonSelected : Theme.Button);
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
                label.Content.Value = item.Count is null ? "<nobr>" + name + "</nobr>" : $"<nobr>{name}   {item.Count}</nobr>";
                SetColor(button, item.Selected ? Theme.ButtonSelected : Theme.Button);
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
        _subtitle.Content.Value = view.Subtitle;
        _count.Content.Value = view.Count;
        _sortButtonLabel.Content.Value = $"{UiStrings.Get("bar.sort")}: {view.SortLabel}";
        _filtersButtonLabel.Content.Value = view.FilterCount > 0 ? $"{UiStrings.Get("bar.filters")} ({view.FilterCount})" : UiStrings.Get("bar.filters");

        RenderChips(view.Chips);
        RenderSortMenu(view);
        RenderFiltersMenu(view);

        var shown = Math.Min(view.Cards.Count, MaxCards);
        for (var i = 0; i < shown; i++)
        {
            if (i >= _cards.Count)
            {
                _cards.Add(CardView.Create(_content, _screen.World, _rounded, _actions));
            }

            _cards[i].Bind(view.Cards[i], _screen.Engine);
        }

        for (var i = shown; i < _cards.Count; i++)
        {
            _cards[i].Release();
        }

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

    private void RenderChips(IReadOnlyList<TagChip> chips)
    {
        for (var i = 0; i < MaxChips; i++)
        {
            var (button, label) = _chips[i];
            if (i < chips.Count)
            {
                var chip = chips[i];
                _chipTags[i] = chip.Tag;
                var mark = chip.State == ChipState.Required ? "+ " : "− ";
                label.Content.Value = mark + chip.Tag;
                SetColor(button, chip.State == ChipState.Excluded ? Theme.ChipExcluded : chip.Personal ? Theme.ChipPersonal : Theme.ChipRequired);
                button.Slot.ActiveSelf = true;
            }
            else
            {
                _chipTags[i] = null;
                button.Slot.ActiveSelf = false;
            }
        }

        _clearChips.Slot.ActiveSelf = chips.Count > 0;
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
    private Button _button = null!;
    private Text _buttonLabel = null!;
    private StaticTexture2D _thumbnail = null!;
    private Uri? _thumbnailUri;

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

        // Thumbnail: world thumbnails are 360 degree images, drawn the way the stock card draws them (verification V10).
        var texture = parts[0].Slot.AttachComponent<StaticTexture2D>();
        var block = parts[0].Slot.AttachComponent<Projection360PropertyBlock>();
        block.Texture.Target = texture;
        block.PerspectiveFieldOfView.Value = float2.One * 110f;
        ui.NestInto(parts[0]);
        ui.RawGraphic(LegacyWorldThumbnailItem.GetWorldPreviewMaterial(world), block);
        card._thumbnail = texture;
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
        card._organize = OverlayButton(ui, UiStrings.Get("card.organize"), new float2(0.66f, 0.04f), new float2(0.97f, 0.28f), out var organizeLabel);
        DiscoverUi.Clamp(organizeLabel, 12f, 18f);
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
        return card;
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

        var status = card.IsOpen ? UiStrings.Get("card.open") : card.Featured ? "★ " + UiStrings.Get("card.featured") : null;
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
        _star.Slot.ActiveSelf = isWorld;
        _organize.Slot.ActiveSelf = isWorld;
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
                BadgeKind.Friends => RadiantUI_Constants.Hero.GREEN_HEX,
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
    }

    /// <summary>Plain text shown literally (verification V11).</summary>
    private static string Safe(string value) => StringParsingHelper.SanitizeFormatTags(value) ?? "";
}
