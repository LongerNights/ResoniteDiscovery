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

/// <summary>
/// The ResoniteDiscovery screen (spec "UI", design in docs/m3-design.md): a lens rail; a top bar with search, sort, filters
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

    /// <summary>The stock Worlds tab's background tile (read from the live stock screen).</summary>
    private static readonly Uri StockBackground = new("resdb:///d54b9c04ef6fd23f0e2b65d3e14fbe036fa8e2b922f336e74c9f115a933cb198.png");

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
        // The stock tab's background: its tiled texture under a Dark tint at 96.5 %. If the texture cannot load, the
        // tint alone is drawn.
        var background = ui.Next("Background");
        var backgroundTexture = background.AttachComponent<StaticTexture2D>();
        backgroundTexture.URL.Value = StockBackground;
        backgroundTexture.WrapModeU.Value = TextureWrapMode.Repeat;
        backgroundTexture.WrapModeV.Value = TextureWrapMode.Repeat;
        var tiled = background.AttachComponent<TiledRawImage>();
        tiled.Texture.Target = backgroundTexture;
        tiled.Tint.Value = Theme.Background;
        tiled.TileSize.Value = new float2(768f, 768f);
        ui.Nest();
        ui.SplitHorizontally(0.16f, out var rail, out var main);
        BuildRail(ui, rail);
        BuildMain(ui, main);

        ApplyTextLift(_screen.Slot);
    }

    /// <summary>
    /// Line-based alignment gives every word the same height whether or not it has a descender, but the line sits
    /// low in its box (measured in game: about 0.17 of the font size), so each text's rect is raised by that much.
    /// Done once per text, after the layout offsets are final; also called for each new card.
    /// </summary>
    internal static void ApplyTextLift(Slot root)
    {
        foreach (var text in root.GetComponentsInChildren<Text>())
        {
            text.AlignmentMode.Value = AlignmentMode.LineBased;
            if (Lifted.TryGetValue(text, out _) || text.Slot.GetComponent<RectTransform>() is not { } rect)
            {
                continue;
            }

            Lifted.Add(text, new object());
            // Fixed and narrowly clamped texts have a known size; a text with a wide auto-size range was never given one.
            var range = text.AutoSizeMax.Value - text.AutoSizeMin.Value;
            var size = text.AutoSizeMax.Value > 0f && range is >= 0f and <= 14f
                ? (text.AutoSizeMin.Value + text.AutoSizeMax.Value) * 0.5f
                : TypeScale.Label;
            var lift = new float2(0f, TextLift * size);
            rect.OffsetMin.Value += lift;
            rect.OffsetMax.Value += lift;
        }
    }

    private const float TextLift = 0.25f;
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Text, object> Lifted = new();

    private void BuildRail(UIBuilder ui, RectTransform rail)
    {
        ui.NestInto(rail);
        ui.Panel(Theme.RailBackground);
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
            _listExtras.Add(DressRailRow(button, label, OfficialAssets.Common.Icons.Folder));
            button.Slot.ActiveSelf = false;
            _listButtons.Add((button, label));
        }

        _newListButton = ui.Button((LocaleString)UiStrings.Get("rail.newList"), Theme.ButtonQuiet);
        Fix(_newListButton.Slot.GetComponentInChildren<Text>(), TypeScale.Label);
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

        // Classic browser: pinned to the bottom of the rail, outside the list so it stays put however long the list is.
        var pin = new UIBuilder(rail);
        RadiantUI_Constants.SetupDefaultStyle(pin);
        pin.Style.ButtonColor = Theme.Button;
        var classic = pin.Button((LocaleString)UiStrings.Get("bar.classic"), Theme.ButtonQuiet);
        Fix(classic.Slot.GetComponentInChildren<Text>(), TypeScale.Body);
        classic.LocalPressed += (_, _) => _actions.OpenClassicBrowser();
        classic.Slot.Name = "ClassicBrowser";
        var classicRect = classic.Slot.GetComponent<RectTransform>();
        classicRect.AnchorMin.Value = new float2(0f, 0f);
        classicRect.AnchorMax.Value = new float2(1f, 0f);
        classicRect.OffsetMin.Value = new float2(14f, 10f);
        classicRect.OffsetMax.Value = new float2(-14f, 54f);
    }

    private readonly Dictionary<string, Slot> _railHeaders = [];
    private readonly List<(Text Count, Slot Bar)> _listExtras = [];
    private Button _newListButton = null!;

    private readonly Dictionary<LensKind, (Text Count, Slot Bar)> _railExtras = [];

    /// <summary>
    /// Makes a rail button a list row: the label left-aligned, a count at the right edge, and a thin accent bar on the
    /// left that shows when the row is the selected one.
    /// </summary>
    private static (Text Count, Slot Bar) DressRailRow(Button button, Text label, Uri? iconUri)
    {
        label.HorizontalAlign.Value = TextHorizontalAlignment.Left;
        Fix(label, TypeScale.Label);
        if (label.Slot.GetComponent<RectTransform>() is { } labelRect)
        {
            labelRect.OffsetMin.Value = new float2(iconUri is null ? 12f : 50f, 0f);
            labelRect.OffsetMax.Value = new float2(-12f, 0f);
        }

        var rect = button.Slot.GetComponent<RectTransform>();
        var inner = new UIBuilder(rect);
        RadiantUI_Constants.SetupDefaultStyle(inner);
        var count = inner.Text((LocaleString)"", bestFit: true, alignment: Alignment.MiddleRight);
        Fix(count, TypeScale.Body);
        count.Color.Value = Theme.TextSecondary;
        count.Slot.GetComponent<RectTransform>()?.AddFixedPadding(12f);
        count.Slot.Name = "Count";

        if (iconUri is not null)
        {
            // The icon is one of the engine's own assets, loaded from the install (nothing is bundled with the mod).
            var texture = button.Slot.AttachComponent<StaticTexture2D>();
            texture.URL.Value = iconUri;
            var sprite = button.Slot.AttachComponent<SpriteProvider>();
            sprite.Texture.Target = texture;
            var icon = inner.Image(Theme.Text);
            icon.Slot.Name = "Icon";
            icon.Sprite.Target = sprite;
            icon.PreserveAspect.Value = true;
            icon.InteractionTarget.Value = false;
            icon.RectTransform.AnchorMax.Value = new float2(0f, 1f);
            icon.RectTransform.OffsetMin.Value = new float2(14f, 6f);
            icon.RectTransform.OffsetMax.Value = new float2(42f, -6f);
        }

        var bar = inner.Image(Theme.Text);
        bar.Slot.Name = "AccentBar";
        bar.InteractionTarget.Value = false;
        bar.RectTransform.AnchorMax.Value = new float2(0f, 1f);
        bar.RectTransform.OffsetMax.Value = new float2(5f, 0f);
        bar.Slot.ActiveSelf = false;
        return (count, bar.Slot);
    }

    /// <summary>The engine icon for each rail entry: the stock world-category icons where the stock tab has one.</summary>
    private static Uri? RailIcon(LensKind lens) => lens switch
    {
        LensKind.CurrentWorlds => OfficialAssets.Graphics.Icons.Dash.NavigationHub,
        LensKind.LiveNow => OfficialAssets.Graphics.Icons.World_Categories.ActiveSessions,
        LensKind.Friends => OfficialAssets.Graphics.Icons.World_Categories.ContactSessions,
        LensKind.Browse => OfficialAssets.Graphics.Icons.World_Categories.Everything,
        LensKind.Featured => OfficialAssets.Graphics.Icons.World_Categories.FeaturedRibbon,
        LensKind.New => OfficialAssets.Graphics.Icons.World_Categories.Published_Worlds,
        LensKind.Random => OfficialAssets.Graphics.Icons.World_Categories.Shuffle,
        LensKind.Favorites => OfficialAssets.Common.Icons.Star,
        LensKind.Recent => OfficialAssets.Common.Icons.Timer,
        LensKind.MyWorlds => OfficialAssets.Graphics.Icons.World_Categories.MyWorlds,
        LensKind.GroupWorlds => OfficialAssets.Graphics.Icons.World_Categories.GroupWorlds,
        LensKind.Trending => OfficialAssets.Common.Icons.Bolt,
        LensKind.Rising => OfficialAssets.Common.Icons.Up_Arrow,
        LensKind.HiddenGems => OfficialAssets.Graphics.Icons.General.Sparks,
        _ => null,
    };

    /// <summary>
    /// Puts one of the engine's own icons at the left of a button, over its centred label (read from the install at
    /// run time; nothing is bundled).
    /// </summary>
    private void AddButtonIcon(Button button, Uri iconUri)
    {
        var texture = button.Slot.AttachComponent<StaticTexture2D>();
        texture.URL.Value = iconUri;
        var sprite = button.Slot.AttachComponent<SpriteProvider>();
        sprite.Texture.Target = texture;
        var inner = new UIBuilder(button.Slot.GetComponent<RectTransform>());
        RadiantUI_Constants.SetupDefaultStyle(inner);
        var icon = inner.Image(Theme.Text);
        icon.Slot.Name = "Icon";
        icon.Sprite.Target = sprite;
        icon.PreserveAspect.Value = true;
        icon.InteractionTarget.Value = false;
        icon.RectTransform.AnchorMin.Value = new float2(0f, 0f);
        icon.RectTransform.AnchorMax.Value = new float2(0f, 1f);
        icon.RectTransform.OffsetMin.Value = new float2(12f, 12f);
        icon.RectTransform.OffsetMax.Value = new float2(40f, -12f);

        // Keep the label clear of the icon.
        if (button.Slot.GetComponentInChildren<Text>()?.Slot.GetComponent<RectTransform>() is { } labelRect)
        {
            labelRect.OffsetMin.Value = new float2(42f, labelRect.OffsetMin.Value.y);
        }
    }

    private void AddRailButton(UIBuilder ui, LensKind lens)
    {
        var button = ui.Button((LocaleString)RailBuilder.Title(lens));
        var text = button.Slot.GetComponentInChildren<Text>();
        button.LocalPressed += (_, _) => _actions.SelectLens(lens);
        _rail[lens] = (button, text);
        _railExtras[lens] = DressRailRow(button, text, RailIcon(lens));
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
        var parts = ui.SplitHorizontally(0.25f, 0.20f, 0.15f, 0.14f, 0.13f, 0.13f);
        foreach (var part in parts)
        {
            part.AddFixedPadding(8f);
        }

        ui.NestInto(parts[0]);
        _search = PromptField(ui, "search.placeholder");
        foreach (var searchText in _search.Slot.GetComponentsInChildren<Text>())
        {
            Fix(searchText, TypeScale.Label);
        }

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
        Clamp(_sortButtonLabel, TypeScale.Body, TypeScale.Label); // long names ("Newest published") shrink a little, short ones stay full size
        _sortBtn = sort;
        sort.LocalPressed += (_, _) => ToggleMenu(_sortMenu);
        ui.NestOut();

        ui.NestInto(parts[2]);
        var filters = ui.Button((LocaleString)"");
        _filtersButtonLabel = filters.Slot.GetComponentInChildren<Text>();
        Clamp(_filtersButtonLabel, TypeScale.Body, TypeScale.Label);
        _filtersBtn = filters;
        filters.LocalPressed += (_, _) => ToggleMenu(_filtersMenu);
        ui.NestOut();

        ui.NestInto(parts[3]);
        var presets = ui.Button((LocaleString)(UiStrings.Get("bar.presets") + " ▾"));
        Fix(presets.Slot.GetComponentInChildren<Text>(), TypeScale.Label);
        _presetsBtn = presets;
        AddButtonIcon(presets, OfficialAssets.Common.Icons.Star);
        presets.LocalPressed += (_, _) => ToggleMenu(_presetsMenu);
        ui.NestOut();

        ui.NestInto(parts[4]);
        var refresh = ui.Button((LocaleString)UiStrings.Get("bar.refresh"));
        Fix(refresh.Slot.GetComponentInChildren<Text>(), TypeScale.Label);
        AddButtonIcon(refresh, OfficialAssets.Common.Icons.Reload);
        refresh.LocalPressed += (_, _) => _actions.Refresh();
        ui.NestOut();

        ui.NestInto(parts[5]);
        var settings = ui.Button((LocaleString)UiStrings.Get("bar.settings"), Theme.ButtonQuiet);
        Fix(settings.Slot.GetComponentInChildren<Text>(), TypeScale.Label);
        AddButtonIcon(settings, OfficialAssets.Common.Icons.Gear);
        settings.LocalPressed += (_, _) => _actions.OpenSettings();
        ui.NestOut();

        ui.NestOut();
    }

    private void BuildChipBar(UIBuilder ui, RectTransform chipBar)
    {
        ui.NestInto(chipBar);
        chipBar.AddFixedPadding(6f);

        _chipBarRect = chipBar;
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

        ui.Style.MinWidth = 0f;
        ui.NestOut(); // the row
        ui.NestOut(); // the chip bar

        // Clear all is not a tag, so it is not in the row: a red button pinned to the bar's right edge.
        var pin = new UIBuilder(chipBar);
        RadiantUI_Constants.SetupDefaultStyle(pin);
        _clearChips = pin.Button((LocaleString)UiStrings.Get("chips.clear"), Theme.ClearButton);
        Fix(_clearChips.Slot.GetComponentInChildren<Text>(), TypeScale.Body);
        _clearChips.Slot.Name = "ClearAll";
        var clearRect = _clearChips.Slot.GetComponent<RectTransform>();
        clearRect.AnchorMin.Value = new float2(1f, 0f);
        clearRect.AnchorMax.Value = new float2(1f, 1f);
        clearRect.OffsetMin.Value = new float2(-134f, 4f);
        clearRect.OffsetMax.Value = new float2(-4f, -4f);
        _clearChips.Slot.ActiveSelf = false;
        _clearChips.LocalPressed += (_, _) => _actions.ClearChips();
    }

    private RectTransform _chipBarRect = null!;

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
        var parts = ui.SplitHorizontally(0.60f, 0.20f, 0.20f);
        _status = TextIn(ui, parts[0], Alignment.MiddleLeft, Theme.TextSecondary, 14f, 20f);
        parts[1].AddFixedPadding(4f);
        parts[2].AddFixedPadding(4f);

        ui.NestInto(parts[1]);
        _previous = ui.Button((LocaleString)UiStrings.Get("nav.previous"));
        Fix(_previous.Slot.GetComponentInChildren<Text>(), TypeScale.Label);
        _previous.LocalPressed += (_, _) => _actions.PreviousPage();
        ui.NestOut();

        ui.NestInto(parts[2]);
        _next = ui.Button((LocaleString)UiStrings.Get("nav.next"), Theme.ButtonSelected);
        Fix(_next.Slot.GetComponentInChildren<Text>(), TypeScale.Label);
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
        BuildStartLensMenu(ui); // after the settings panel, so it draws over it
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
        AnimateOpen(_confirmPanel, ModalRise);
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
        AnimateOpen(_newSessionPanel, ModalRise);
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
            var captured = key;
            button.LocalPressed += (_, _) => { _actions.ChooseSort(captured); CloseMenus(); };
            _sortItems[key] = (button, button.Slot.GetComponentInChildren<Text>());
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
        return button;
    }

    /// <summary>Slides a switch's knob to the on or off end and colours its track.</summary>
    private static void ApplySwitch(Image track, RectTransform knob, bool on)
    {
        track.Tint.Value = on ? Theme.ButtonSelected : Theme.Rail;
        knob.AnchorMin.Value = on ? new float2(1f, 0f) : new float2(0f, 0f);
        knob.AnchorMax.Value = on ? new float2(1f, 1f) : new float2(0f, 1f);
        knob.OffsetMin.Value = on ? new float2(-25f, 3f) : new float2(3f, 3f);
        knob.OffsetMax.Value = on ? new float2(-3f, -3f) : new float2(25f, -3f);
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
            UniLog.Log($"[Discover] Popover placed: centre={centre:F2} left={left:F2} top={top:F2}");
        }
        catch (Exception ex)
        {
            UniLog.Warning($"[Discover] Placing a popover failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Panel open animations (the Animations setting). Off: panels just appear.</summary>
    public bool Animate { get; set; } = true;

    /// <summary>
    /// Slides a panel into place by tweening its anchors from a small offset (<paramref name="from"/>, in fractions of the
    /// pane) to where they are. Only the anchors move: the panel's size comes from its layout, and its tint is driven by
    /// its press-absorbing button, so neither can be tweened.
    /// </summary>
    private void AnimateOpen(Slot panel, float2 from)
    {
        if (!Animate || panel.GetComponent<RectTransform>() is not { } rect)
        {
            return;
        }

        try
        {
            var min = rect.AnchorMin.Value;
            var max = rect.AnchorMax.Value;
            rect.AnchorMin.TweenFromTo(min + from, min, 0.18f, CurvePreset.CubicOut);
            rect.AnchorMax.TweenFromTo(max + from, max, 0.18f, CurvePreset.CubicOut);
        }
        catch (Exception ex)
        {
            UniLog.Warning($"[Discover] Panel animation failed: {ex.GetType().Name}: {ex.Message}");
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
            AnimateOpen(menu, MenuDrop);
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
    private readonly List<(Image Track, RectTransform Knob)> _organizeListSwitches = [];
    private readonly string?[] _organizeListIds = new string?[MaxOrganizeLists];
    private readonly List<(Button Button, Text Label)> _organizeTags = [];
    private readonly string?[] _organizeTagNames = new string?[MaxOrganizeTags];

    private void BuildOrganize(UIBuilder ui)
    {
        _organizeScrim = Catcher(ui, "OrganizeScrim", Theme.ConfirmScrim, CloseMenus);

        var slot = BeginFloating(ui, "Organize", 0.18f, 0.82f, 0.5f, 0.5f, 8f, 22f);

        ui.Style.MinHeight = 50f;
        _organizeTitle = ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.MiddleCenter);
        Fix(_organizeTitle, TypeScale.Title);
        _organizeTitle.Color.Value = Theme.Heading;

        ui.Style.MinHeight = 32f;
        var lists = ui.Text((LocaleString)UiStrings.Get("organize.lists").ToUpperInvariant(), bestFit: true, alignment: Alignment.BottomLeft);
        Fix(lists, TypeScale.Body);
        lists.Color.Value = Theme.TextSecondary;

        ui.Style.MinHeight = 40f;
        _organizeNoLists = ui.Text((LocaleString)UiStrings.Get("organize.noLists"), bestFit: true, alignment: Alignment.MiddleLeft);
        Fix(_organizeNoLists, TypeScale.Body);
        _organizeNoLists.Color.Value = Theme.TextSecondary;

        ui.Style.MinHeight = 42f;
        for (var i = 0; i < MaxOrganizeLists; i++)
        {
            var button = SwitchButton(ui, "", out var label, out var track, out var knob);
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
            _organizeListSwitches.Add((track, knob));
        }

        var newList = ui.Button((LocaleString)UiStrings.Get("rail.newList"), Theme.ButtonQuiet);
        Fix(newList.Slot.GetComponentInChildren<Text>(), TypeScale.Label);
        newList.LocalPressed += (_, _) => _actions.OrganizeNewList();

        ui.Style.MinHeight = 32f;
        var tags = ui.Text((LocaleString)UiStrings.Get("organize.tags").ToUpperInvariant(), bestFit: true, alignment: Alignment.BottomLeft);
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
            Fix(label, TypeScale.Body);
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
        Fix(add.Slot.GetComponentInChildren<Text>(), TypeScale.Body);
        add.LocalPressed += (_, _) => SubmitTag();
        ui.Style.MinWidth = 0f;
        ui.NestOut();

        ui.Style.MinHeight = 32f;
        var suggested = ui.Text((LocaleString)UiStrings.Get("organize.suggested").ToUpperInvariant(), bestFit: true, alignment: Alignment.BottomLeft);
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
            Fix(label, TypeScale.Body);
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
        Fix(done.Slot.GetComponentInChildren<Text>(), TypeScale.Label);
        done.LocalPressed += (_, _) => CloseMenus();

        ui.NestOut(); // the panel
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
                if (button.Slot.GetComponent<LayoutElement>() is { } suggestLayout)
                {
                    suggestLayout.MinWidth.Value = Math.Clamp(Math.Min(found[i].Length, 12) * 11f + 54f, 96f, 220f);
                }
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
        foreach (var fieldText in field.Slot.GetComponentsInChildren<Text>())
        {
            Fix(fieldText, TypeScale.Body);
            fieldText.HorizontalAlign.Value = TextHorizontalAlignment.Left;
            if (fieldText.Slot.GetComponent<RectTransform>() is { } fieldRect)
            {
                fieldRect.OffsetMin.Value = new float2(14f, 0f);
            }
        }

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
                label.Content.Value = "<nobr>" + Plain(CardText.Ellipsize(list.Name, 28)) + "</nobr>";
                ApplySwitch(_organizeListSwitches[i].Track, _organizeListSwitches[i].Knob, list.Member);
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
                if (button.Slot.GetComponent<LayoutElement>() is { } chipLayout)
                {
                    chipLayout.MinWidth.Value = Math.Clamp(Math.Min(model.Tags[i].Length, 12) * 11f + 54f, 96f, 220f);
                }
            }
        }

        RefreshTagSuggestions();
        var wasOpen = _organizePanel.ActiveSelf;
        _organizeScrim.ActiveSelf = true;
        _organizePanel.ActiveSelf = true;
        if (!wasOpen)
        {
            AnimateOpen(_organizePanel, ModalRise);
        }
    }

    // ---- detail panel (spec F18): everything known about one world, with Join and Lists

    private Slot _detailScrim = null!;
    private Slot _detailPanel = null!;
    private Text _detailTitle = null!;
    private Text _detailBy = null!;
    private Text _detailDescription = null!;
    private Text _detailTags = null!;
    private Text _detailSessions = null!;
    private readonly List<(Button Button, Text Label)> _detailSessionRows = [];
    private IReadOnlyList<DetailSession> _detailSessionData = [];
    private int _detailSelected;
    private DetailModel? _detailModel;
    private HorizontalLayout _detailSessionArea = null!;
    private Slot _detailSessionInfoFrame = null!;
    private Button _detailNewSession = null!;
    private Text _detailUptime = null!;
    private Text _detailVisibility = null!;
    private Text _detailUsersLine = null!;
    private Text _detailPeople = null!;
    private static readonly string ContactColorHex = "#" + RadiantUI_Constants.Hero.GREEN.ToHexString(false).TrimStart('#');
    private Text _detailDevices = null!;
    private Text _detailDates = null!;
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

    private const int MaxDetailTags = 10;
    private readonly List<(Button Button, Text Label)> _detailTagButtons = [];
    private readonly string?[] _detailTagNames = new string?[MaxDetailTags];
    private Text _detailTagsHeader = null!;
    private Slot _detailTagGrid = null!;
    private StaticTexture2D _detailThumbnail = null!;
    private Uri? _detailThumbnailUri;

    private void BuildDetails(UIBuilder ui)
    {
        _detailScrim = Catcher(ui, "DetailScrim", Theme.ConfirmScrim, CloseMenus);

        var slot = BeginFloating(ui, "Detail", 0.14f, 0.86f, 0.5f, 0.5f, 8f, 18f);

        // Top: the world's 360 preview (pans on hover, as on the card) beside its name, byline and description.
        ui.Style.MinHeight = 170f;
        var top = ui.HorizontalLayout(18f, 0f, Alignment.TopLeft);
        top.ForceExpandWidth.Value = false;
        ui.Style.MinWidth = 400f;
        ui.Style.FlexibleWidth = -1f;
        var previewRect = ui.Next("Preview").GetComponent<RectTransform>();
        BuildDetailPreview(ui, previewRect);

        ui.Style.MinWidth = 0f;
        ui.Style.FlexibleWidth = 1f;
        var info = ui.VerticalLayout(6f, 0f, Alignment.TopLeft);
        info.ForceExpandHeight.Value = false;
        ui.Style.FlexibleWidth = -1f;

        ui.Style.MinHeight = 48f;
        _detailTitle = ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.MiddleLeft);
        Fix(_detailTitle, TypeScale.Title);
        _detailTitle.Color.Value = Theme.Heading;

        ui.Style.MinHeight = 28f;
        _detailBy = ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.MiddleLeft);
        Fix(_detailBy, TypeScale.Body);
        _detailBy.Color.Value = Theme.TextSecondary;

        ui.Style.MinHeight = 50f;
        _detailDescription = ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.TopLeft);
        Fix(_detailDescription, TypeScale.Body);

        ui.Style.MinHeight = 24f;
        _detailDates = ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.MiddleLeft);
        Fix(_detailDates, TypeScale.Caption);
        _detailDates.Color.Value = Theme.TextSecondary;

        ui.Style.MinHeight = 26f;
        _detailDevices = ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.MiddleLeft);
        Fix(_detailDevices, TypeScale.Caption);
        _detailDevices.Color.Value = Theme.TextSecondary;
        ui.NestOut(); // info
        ui.NestOut(); // top

        // Tags: chips; pressing one adds it to the tag chips on the results and closes the panel.
        ui.Style.MinHeight = 26f;
        _detailTagsHeader = ui.Text((LocaleString)UiStrings.Get("detail.tags"), bestFit: true, alignment: Alignment.BottomLeft);
        Fix(_detailTagsHeader, TypeScale.Caption);
        _detailTagsHeader.Color.Value = Theme.TextSecondary;

        ui.Style.MinHeight = 88f;
        var tagArea = ui.Empty("Tags");
        var grid = tagArea.AttachComponent<GridLayout>();
        grid.ChildAlignment = Alignment.TopLeft;
        grid.CellSize.Value = new float2(150f, 38f);
        grid.Spacing.Value = new float2(8f, 8f);
        _detailTagGrid = tagArea;
        ui.NestInto(tagArea.GetComponent<RectTransform>());
        for (var i = 0; i < MaxDetailTags; i++)
        {
            var chip = ui.Button((LocaleString)"");
            var label = chip.Slot.GetComponentInChildren<Text>();
            Fix(label, TypeScale.Body);
            var index = i;
            chip.LocalPressed += (_, _) =>
            {
                if (_detailTagNames[index] is { } tag)
                {
                    CloseMenus();
                    _actions.CycleChip(tag);
                }
            };
            chip.Slot.ActiveSelf = false;
            _detailTagButtons.Add((chip, label));
        }

        ui.NestOut();

        ui.Style.MinHeight = 26f;
        var sessionsHeader = ui.Text((LocaleString)UiStrings.Get("detail.sessions"), bestFit: true, alignment: Alignment.BottomLeft);
        Fix(sessionsHeader, TypeScale.Caption);
        sessionsHeader.Color.Value = Theme.TextSecondary;
        // Sessions, as on the stock tab: the list at the left, and the information for the chosen one at the right.
        ui.Style.MinHeight = 188f;
        _detailSessionArea = ui.HorizontalLayout(12f, 0f, Alignment.TopLeft);
        _detailSessionArea.ForceExpandWidth.Value = false;
        ui.Style.MinWidth = 270f;
        ui.Style.FlexibleWidth = -1f;
        var list = ui.VerticalLayout(5f, 0f, Alignment.TopCenter);
        list.ForceExpandHeight.Value = false;
        ui.Style.MinHeight = 40f;
        for (var i = 0; i < DetailBuilder.MaxSessionRows; i++)
        {
            var pick = ui.Button((LocaleString)"");
            var pickLabel = pick.Slot.GetComponentInChildren<Text>();
            Fix(pickLabel, TypeScale.Body);
            pickLabel.HorizontalAlign.Value = TextHorizontalAlignment.Left;
            if (pickLabel.Slot.GetComponent<RectTransform>() is { } pickRect)
            {
                pickRect.OffsetMin.Value = new float2(14f, 0f);
            }

            var index = i;
            pick.LocalPressed += (_, _) => SelectDetailSession(index);
            pick.Slot.ActiveSelf = false;
            _detailSessionRows.Add((pick, pickLabel));
        }

        _detailNewSession = ui.Button((LocaleString)UiStrings.Get("detail.newSession"), Theme.ButtonQuiet);
        Fix(_detailNewSession.Slot.GetComponentInChildren<Text>(), TypeScale.Body);
        _detailNewSession.LocalPressed += (_, _) =>
        {
            if (_detailKey is { } key)
            {
                CloseMenus();
                _actions.StartNewSession(key);
            }
        };
        ui.NestOut(); // the list

        ui.Style.MinWidth = 0f;
        ui.Style.FlexibleWidth = 1f;
        var infoFrame = ui.Panel(Theme.Card, _rounded, NineSliceSizing.FixedSize);
        var infoLayout = ui.VerticalLayout(4f, 12f, Alignment.TopLeft);
        infoLayout.ForceExpandHeight.Value = false;
        ui.Style.FlexibleWidth = -1f;
        ui.Style.MinHeight = 28f;
        _detailUptime = ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.MiddleLeft);
        Fix(_detailUptime, TypeScale.Body);
        _detailVisibility = ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.MiddleLeft);
        Fix(_detailVisibility, TypeScale.Body);
        _detailUsersLine = ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.MiddleLeft);
        Fix(_detailUsersLine, TypeScale.Body);

        // Who is in: contacts first and in green, at the same size as the rest.
        ui.Style.MinHeight = 76f;
        _detailPeople = ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.TopLeft);
        Fix(_detailPeople, TypeScale.Body);
        ui.NestOut(); // the info layout
        ui.NestOut(); // the info frame
        ui.NestOut(); // the session area
        _detailSessionInfoFrame = infoFrame.Slot;

        ui.Style.MinHeight = 40f;
        _detailSessions = ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.MiddleLeft);
        Fix(_detailSessions, TypeScale.Body);

        // Actions: the main one with the everyday ones beside it, then the quieter ones.
        ui.Style.MinHeight = 50f;
        var row = ui.HorizontalLayout(10f, 0f, Alignment.MiddleCenter);
        _detailAction = ui.Button((LocaleString)"", Theme.ButtonSelected);
        _detailActionLabel = _detailAction.Slot.GetComponentInChildren<Text>();
        Fix(_detailActionLabel, TypeScale.Label);
        _detailAction.LocalPressed += (_, _) =>
        {
            if (_detailKey is not { } key)
            {
                return;
            }

            CloseMenus();

            // With live sessions the main button joins the one chosen in the list.
            if (_detailModel is { Action: CardAction.Join } && _detailSessionData.Count > 0 && _detailSelected < _detailSessionData.Count)
            {
                _actions.JoinSession(key, _detailSessionData[_detailSelected].SessionId);
            }
            else
            {
                _actions.CardPressed(key);
            }
        };
        _detailFavorite = ui.Button((LocaleString)UiStrings.Get("detail.favorite"));
        _detailFavoriteLabel = _detailFavorite.Slot.GetComponentInChildren<Text>();
        Fix(_detailFavoriteLabel, TypeScale.Body);
        _detailFavorite.LocalPressed += (_, _) =>
        {
            if (_detailKey is { } key)
            {
                _actions.ToggleFavorite(key);
                _detailFavorited = !_detailFavorited;
                _detailFavoriteLabel.Content.Value = UiStrings.Get(_detailFavorited ? "detail.unfavorite" : "detail.favorite");
            }
        };
        _detailLists = ui.Button((LocaleString)UiStrings.Get("card.organize"));
        Fix(_detailLists.Slot.GetComponentInChildren<Text>(), TypeScale.Body);
        _detailLists.LocalPressed += (_, _) =>
        {
            if (_detailKey is { } key)
            {
                CloseMenus();
                _actions.OpenOrganize(key);
            }
        };
        _detailCopy = ui.Button((LocaleString)UiStrings.Get("detail.copyLink"));
        Fix(_detailCopy.Slot.GetComponentInChildren<Text>(), TypeScale.Body);
        _detailCopy.LocalPressed += (_, _) =>
        {
            if (_detailKey is { } key)
            {
                _actions.CopyLink(key);
            }
        };
        ui.NestOut();

        ui.Style.MinHeight = 44f;
        var quiet = ui.HorizontalLayout(10f, 0f, Alignment.MiddleCenter);
        _detailAuthor = ui.Button((LocaleString)UiStrings.Get("detail.moreByAuthor"), Theme.ButtonQuiet);
        Fix(_detailAuthor.Slot.GetComponentInChildren<Text>(), TypeScale.Body);
        _detailAuthor.LocalPressed += (_, _) =>
        {
            if (_detailKey is { } key)
            {
                CloseMenus();
                _actions.MoreByAuthor(key);
            }
        };
        _detailHide = ui.Button((LocaleString)UiStrings.Get("detail.hide"), Theme.ButtonQuiet);
        Fix(_detailHide.Slot.GetComponentInChildren<Text>(), TypeScale.Body);
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
        Fix(_detailLeaveLabel, TypeScale.Body);
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
        Fix(close.Slot.GetComponentInChildren<Text>(), TypeScale.Body);
        close.LocalPressed += (_, _) => CloseMenus();
        ui.NestOut();

        ui.NestOut(); // the panel
        slot.ActiveSelf = false;
        _detailPanel = slot;
    }

    /// <summary>The detail panel's 360 preview: drawn like the card's, and panned by hovering it.</summary>
    private void BuildDetailPreview(UIBuilder ui, RectTransform previewRect)
    {
        var texture = previewRect.Slot.AttachComponent<StaticTexture2D>();
        var block = previewRect.Slot.AttachComponent<Projection360PropertyBlock>();
        block.Texture.Target = texture;
        block.PerspectiveFieldOfView.Value = float2.One * 110f;
        _detailThumbnail = texture;

        ui.NestInto(previewRect);
        ui.RawGraphic(LegacyWorldThumbnailItem.GetWorldPreviewMaterial(_screen.World), block);
        var catcher = ui.Image(colorX.Black.SetA(0.02f));
        catcher.Slot.Name = "Hover";
        var hover = catcher.Slot.AttachComponent<Button>();
        var driver = hover.ColorDrivers[0];
        driver.TintColorMode.Value = InteractionElement.ColorMode.Direct;
        driver.NormalColor.Value = colorX.Black.SetA(0.02f);
        driver.HighlightColor.Value = colorX.Black.SetA(0.02f);
        driver.PressColor.Value = colorX.Black.SetA(0.02f);
        driver.DisabledColor.Value = colorX.Black.SetA(0.02f);
        var panner = catcher.Slot.AttachComponent<Perspective360Panner>();
        block.PerspectiveAngleOffset.DriveFrom(panner.AngleOffset);
        block.PerspectiveFieldOfView.DriveFrom(panner.FOV);
        ui.NestOut();
    }

    private void SetDetailThumbnail(string? raw)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
        {
            uri = OfficialAssets.Skyboxes.Thumbnails.NoThumbnail;
        }

        if (uri == _detailThumbnailUri)
        {
            return;
        }

        var direct = uri.Scheme != _screen.Engine.Cloud.Assets.DBScheme;
        _detailThumbnail.Uncompressed.Value = direct;
        _detailThumbnail.DirectLoad.Value = direct;
        _detailThumbnail.URL.Value = uri;
        _detailThumbnailUri = uri;
    }

    private void SelectDetailSession(int index)
    {
        if (index < _detailSessionData.Count)
        {
            _detailSelected = index;
            RenderDetailSession();
        }
    }

    /// <summary>Marks the chosen session in the list, shows its information, and points the main button at it.</summary>
    private void RenderDetailSession()
    {
        for (var r = 0; r < _detailSessionRows.Count; r++)
        {
            SetColor(_detailSessionRows[r].Button, r == _detailSelected ? Theme.ButtonSelected : Theme.Button);
        }

        if (_detailSelected >= _detailSessionData.Count)
        {
            return;
        }

        var session = _detailSessionData[_detailSelected];
        _detailUptime.Content.Value = "<nobr>" + Plain(session.Uptime) + "</nobr>";
        _detailVisibility.Content.Value = "<nobr>" + Plain(session.Visibility) + "</nobr>";
        _detailUsersLine.Content.Value = "<nobr>" + Plain(session.UsersLine) + "</nobr>";
        var names = session.Users.Take(24).Select(u => u.IsContact
            ? $"<color={ContactColorHex}>{Plain(CardText.Ellipsize(u.Name, 20))}</color>"
            : Plain(CardText.Ellipsize(u.Name, 20)));
        _detailPeople.Content.Value = string.Join(", ", names);

        // A joinable session is joined by the main button; a full one says so.
        if (_detailModel is { Action: CardAction.Join })
        {
            _detailActionLabel.Content.Value = UiStrings.Get(session.CanJoin ? "card.join" : "card.full");
            _detailAction.Enabled = session.CanJoin;
            _detailAction.ColorDrivers[0].SetColors(session.CanJoin ? Theme.ButtonSelected : Theme.ButtonQuiet);
        }
    }

    public void ShowDetails(DetailModel model)
    {
        CloseMenus();
        _detailKey = model.Key;
        _detailTitle.Content.Value = "<nobr>" + Plain(model.Title) + "</nobr>";
        _detailBy.Content.Value = "<nobr>" + Plain(model.ByLine) + "</nobr>";
        _detailDescription.Content.Value = Plain(model.Description);
        var tagList = model.TagList ?? [];
        for (var t = 0; t < MaxDetailTags; t++)
        {
            var show = t < tagList.Count;
            _detailTagNames[t] = show ? tagList[t] : null;
            _detailTagButtons[t].Button.Slot.ActiveSelf = show;
            _detailTagButtons[t].Label.Content.Value = show ? "<nobr>" + Plain(CardText.Ellipsize(tagList[t], 14)) + "</nobr>" : "";
        }

        _detailTagsHeader.Slot.ActiveSelf = tagList.Count > 0;
        _detailTagGrid.ActiveSelf = tagList.Count > 0;
        SetDetailThumbnail(model.ThumbnailUrl);
        _detailModel = model;
        _detailSessionData = model.SessionRows ?? [];
        _detailSelected = 0;
        _detailSessionArea.Slot.ActiveSelf = _detailSessionData.Count > 0;
        _detailNewSession.Slot.ActiveSelf = model.CanStartNew && _detailSessionData.Count > 0;
        for (var r = 0; r < _detailSessionRows.Count; r++)
        {
            var show = r < _detailSessionData.Count;
            _detailSessionRows[r].Button.Slot.ActiveSelf = show;
            if (show)
            {
                _detailSessionRows[r].Label.Content.Value = "<nobr>" + Plain(_detailSessionData[r].ListText) + "</nobr>";
            }
        }

        _detailSessions.Slot.ActiveSelf = _detailSessionData.Count == 0;
        RenderDetailSession();
        _detailSessions.Content.Value = Plain(model.Sessions);
        _detailDevices.Content.Value = Plain(model.Devices);
        _detailDates.Content.Value = "<nobr>" + Plain(model.Dates) + "</nobr>";
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
        RenderDetailSession();
        _detailScrim.ActiveSelf = true;
        _detailPanel.ActiveSelf = true;
        AnimateOpen(_detailPanel, ModalRise);
    }

    // ---- settings panel: the sidebar's sections and lenses on the left, general options and your data on the right

    private Slot _settingsScrim = null!;
    private Slot _settingsPanel = null!;
    private readonly List<Slot> _settingsPages = [];
    private readonly List<Button> _settingsTabs = [];
    private int _settingsTab;
    private bool _settingsListView;

    // Page controls, bound by ShowSettings.
    private (Image Track, RectTransform Knob) _animationsSwitch;
    private (Image Track, RectTransform Knob) _liveOnlySwitch;
    private (Image Track, RectTransform Knob) _notifyLiveSwitch;
    private (Image Track, RectTransform Knob) _notifyContactsSwitch;
    private Text _startLensValue = null!;
    private Button _startLensRow = null!;
    private Slot _startLensMenu = null!;
    private const int MaxStartLensRows = 16;
    private readonly List<(Button Button, Text Label)> _startLensItems = [];
    private IReadOnlyList<(LensKind Lens, string Label, bool Selected)> _startLensChoices = [];
    private Button _viewGrid = null!;
    private Button _viewList = null!;
    private readonly Dictionary<int, Button> _cardsPerRowButtons = [];
    private Text _hiddenCounts = null!;
    private const int MaxHiddenRows = 6;
    private readonly List<(Slot Row, Text Label)> _hiddenRows = [];
    private Text _hiddenMore = null!;
    private IReadOnlyList<HiddenItem> _hiddenItems = [];
    private readonly Dictionary<RailSectionId, (Button Button, Image Track, RectTransform Knob)> _sectionRows = [];
    private readonly Dictionary<LensKind, (Button Button, Image Track, RectTransform Knob)> _lensRows = [];

    private void BuildSettings(UIBuilder ui)
    {
        _settingsScrim = Catcher(ui, "SettingsScrim", Theme.ConfirmScrim, CloseMenus);
        var slot = BeginFloating(ui, "Settings", 0.12f, 0.88f, 0.5f, 0.5f, 10f, 22f);

        ui.Style.MinHeight = 48f;
        var title = ui.Text((LocaleString)UiStrings.Get("settings.title"), bestFit: true, alignment: Alignment.MiddleLeft);
        Fix(title, TypeScale.Title);
        title.Color.Value = Theme.Heading;

        // Tabs at the left, the chosen page at the right; the height is the same for every page.
        ui.Style.MinHeight = 440f;
        var body = ui.HorizontalLayout(18f, 0f, Alignment.TopLeft);
        body.ForceExpandWidth.Value = false;

        ui.Style.MinWidth = 190f;
        ui.Style.FlexibleWidth = -1f;
        var tabs = ui.VerticalLayout(6f, 0f, Alignment.TopCenter);
        tabs.ForceExpandHeight.Value = false;
        ui.Style.MinHeight = 46f;
        foreach (var (id, index) in new[] { "settings.tab.general", "settings.tab.display", "settings.tab.sidebar", "settings.tab.alerts", "settings.tab.hidden", "settings.tab.data" }.Select((id, index) => (id, index)))
        {
            var tab = ui.Button((LocaleString)UiStrings.Get(id));
            Fix(tab.Slot.GetComponentInChildren<Text>(), TypeScale.Body);
            tab.Slot.GetComponentInChildren<Text>().HorizontalAlign.Value = TextHorizontalAlignment.Left;
            if (tab.Slot.GetComponentInChildren<Text>().Slot.GetComponent<RectTransform>() is { } tabRect)
            {
                tabRect.OffsetMin.Value = new float2(16f, 0f);
            }

            var captured = index;
            tab.LocalPressed += (_, _) => ShowSettingsTab(captured);
            _settingsTabs.Add(tab);
        }

        ui.NestOut();

        ui.Style.MinWidth = 0f;
        ui.Style.FlexibleWidth = 1f;
        var pages = ui.Next("Pages").GetComponent<RectTransform>();
        ui.Style.FlexibleWidth = -1f;
        ui.NestInto(pages);

        // General
        _settingsPages.Add(SettingsPage(ui, () =>
        {
            SettingsGroup(ui, "settings.tab.general");
            _startLensRow = ValueRow(ui, UiStrings.Get("settings.startOn"), ToggleStartLensMenu, out _startLensValue);
            _liveOnlySwitch = SettingsSwitch(ui, UiStrings.Get("settings.liveOnly"), () => _actions.ToggleSetting(SettingKey.LiveOnly));
            _animationsSwitch = SettingsSwitch(ui, UiStrings.Get("settings.animations"), () => _actions.ToggleSetting(SettingKey.Animations));
        }));

        // Display
        _settingsPages.Add(SettingsPage(ui, () =>
        {
            SettingsGroup(ui, "settings.tab.display");
            SettingsLabel(ui, UiStrings.Get("settings.view"));
            ui.Style.MinHeight = 46f;
            var layoutRow = ui.HorizontalLayout(6f, 0f, Alignment.MiddleCenter);
            _viewGrid = ui.Button((LocaleString)UiStrings.Get("settings.viewGrid"));
            Fix(_viewGrid.Slot.GetComponentInChildren<Text>(), TypeScale.Body);
            _viewGrid.LocalPressed += (_, _) => _actions.SetListView(false);
            _viewList = ui.Button((LocaleString)UiStrings.Get("settings.viewList"));
            Fix(_viewList.Slot.GetComponentInChildren<Text>(), TypeScale.Body);
            _viewList.LocalPressed += (_, _) => _actions.SetListView(true);
            ui.NestOut();

            SettingsLabel(ui, UiStrings.Get("settings.cardsPerRow"));
            ui.Style.MinHeight = 46f;
            var cardsRow = ui.HorizontalLayout(6f, 0f, Alignment.MiddleCenter);
            foreach (var count in DiscoverSettings.CardsPerRowChoices)
            {
                var button = ui.Button((LocaleString)count.ToString());
                Fix(button.Slot.GetComponentInChildren<Text>(), TypeScale.Body);
                var captured = count;
                button.LocalPressed += (_, _) => _actions.SetCardsPerRow(captured);
                _cardsPerRowButtons[count] = button;
            }

            ui.NestOut();
        }));

        // Sidebar: two columns, since there are two dozen switches.
        _settingsPages.Add(SettingsPage(ui, () =>
        {
            ui.Style.MinHeight = 0f;
            ui.Style.FlexibleHeight = 1f;
            var columns = ui.HorizontalLayout(14f, 0f, Alignment.TopLeft);
            columns.ForceExpandHeight.Value = false;
            ui.Style.FlexibleHeight = -1f;
            foreach (var group in new[] { new[] { RailSectionId.Now, RailSectionId.Discover }, new[] { RailSectionId.Yours, RailSectionId.Insights } })
            {
                ui.Style.FlexibleWidth = 1f;
                var column = ui.VerticalLayout(4f, 0f, Alignment.TopCenter);
                column.ForceExpandHeight.Value = false;
                ui.Style.FlexibleWidth = -1f;
                var firstSection = true;
                foreach (var section in group)
                {
                    if (!firstSection)
                    {
                        ui.Style.MinHeight = 14f; // a gap between sections
                        ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.MiddleCenter);
                    }

                    firstSection = false;
                    ui.Style.MinHeight = 42f;
                    var id = section;
                    var sectionRow = SettingsSwitchRow(ui, "", () => _actions.ToggleSection(id));
                    _sectionRows[section] = (sectionRow.Button, sectionRow.Track, sectionRow.Knob);
                    foreach (var lens in LensCatalog.LensesOf(section))
                    {
                        ui.Style.MinHeight = 36f;
                        var kind = lens;
                        var lensRow = SettingsSwitchRow(ui, "", () => _actions.ToggleLens(kind));
                        if (lensRow.Label.Slot.GetComponent<RectTransform>() is { } lensRect)
                        {
                            lensRect.OffsetMin.Value = new float2(36f, 0f);
                        }

                        _lensRows[lens] = (lensRow.Button, lensRow.Track, lensRow.Knob);
                    }
                }

                ui.NestOut();
            }

            ui.NestOut();
        }));

        // Alerts
        _settingsPages.Add(SettingsPage(ui, () =>
        {
            SettingsGroup(ui, "settings.tab.alerts");
            _notifyLiveSwitch = SettingsSwitch(ui, UiStrings.Get("settings.notifyLive"), () => _actions.ToggleSetting(SettingKey.NotifyLive));
            _notifyContactsSwitch = SettingsSwitch(ui, UiStrings.Get("settings.notifyContacts"), () => _actions.ToggleSetting(SettingKey.NotifyContacts));
            ui.Style.MinHeight = 56f;
            var note = ui.Text((LocaleString)UiStrings.Get("settings.alertsNote"), bestFit: true, alignment: Alignment.TopLeft);
            Fix(note, TypeScale.Caption);
            note.Color.Value = Theme.TextSecondary;
        }));

        // Hidden
        _settingsPages.Add(SettingsPage(ui, () =>
        {
            SettingsGroup(ui, "settings.tab.hidden");
            ui.Style.MinHeight = 44f;
            _hiddenCounts = ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.MiddleLeft);
            Fix(_hiddenCounts, TypeScale.Body);
            ui.Style.MinHeight = 46f;
            var hiddenButtons = ui.HorizontalLayout(8f, 0f, Alignment.MiddleCenter);
            var add = ui.Button((LocaleString)UiStrings.Get("settings.hiddenWord"));
            Fix(add.Slot.GetComponentInChildren<Text>(), TypeScale.Body);
            add.LocalPressed += (_, _) => _actions.ToggleSetting(SettingKey.HiddenWord);
            var restore = ui.Button((LocaleString)UiStrings.Get("settings.hiddenRestoreAll"), Theme.ButtonQuiet);
            Fix(restore.Slot.GetComponentInChildren<Text>(), TypeScale.Body);
            restore.LocalPressed += (_, _) => _actions.ToggleSetting(SettingKey.HiddenRestore);
            ui.NestOut();

            // What is hidden, by name, each with a button to bring just that one back.
            ui.Style.MinHeight = 38f;
            for (var i = 0; i < MaxHiddenRows; i++)
            {
                var hiddenRow = ui.HorizontalLayout(8f, 0f, Alignment.MiddleLeft);
                ui.Style.FlexibleWidth = 1f;
                var text = ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.MiddleLeft);
                Fix(text, TypeScale.Body);
                ui.Style.FlexibleWidth = -1f;
                ui.Style.MinWidth = 52f;
                var remove = ui.Button((LocaleString)"✕", Theme.ButtonQuiet);
                Fix(remove.Slot.GetComponentInChildren<Text>(), TypeScale.Body);
                ui.Style.MinWidth = 0f;
                var index = i;
                remove.LocalPressed += (_, _) =>
                {
                    if (index < _hiddenItems.Count)
                    {
                        _actions.RestoreHidden(_hiddenItems[index].Kind, _hiddenItems[index].Id);
                    }
                };
                ui.NestOut();
                hiddenRow.Slot.ActiveSelf = false;
                _hiddenRows.Add((hiddenRow.Slot, text));
            }

            ui.Style.MinHeight = 28f;
            _hiddenMore = ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.MiddleLeft);
            Fix(_hiddenMore, TypeScale.Caption);
            _hiddenMore.Color.Value = Theme.TextSecondary;
        }));

        // Data
        _settingsPages.Add(SettingsPage(ui, () =>
        {
            SettingsGroup(ui, "settings.tab.data");
            ui.Style.MinHeight = 50f;
            var row = ui.HorizontalLayout(10f, 0f, Alignment.MiddleCenter);
            var export = ui.Button((LocaleString)UiStrings.Get("settings.export"));
            Fix(export.Slot.GetComponentInChildren<Text>(), TypeScale.Label);
            export.LocalPressed += (_, _) => _actions.ExportLibrary();
            var import = ui.Button((LocaleString)UiStrings.Get("settings.import"));
            Fix(import.Slot.GetComponentInChildren<Text>(), TypeScale.Label);
            import.LocalPressed += (_, _) => _actions.ImportLibrary();
            ui.NestOut();
            ui.Style.MinHeight = 90f;
            var note = ui.Text((LocaleString)UiStrings.Get("settings.note"), bestFit: true, alignment: Alignment.TopLeft);
            Fix(note, TypeScale.Caption);
            note.Color.Value = Theme.TextSecondary;
        }));

        ui.NestOut(); // pages
        ui.NestOut(); // body

        ui.Style.MinHeight = 50f;
        var footer = ui.HorizontalLayout(10f, 0f, Alignment.MiddleRight);
        footer.ForceExpandWidth.Value = false;
        ui.Style.MinWidth = 160f;
        var done = ui.Button((LocaleString)UiStrings.Get("organize.done"), Theme.ButtonSelected);
        Fix(done.Slot.GetComponentInChildren<Text>(), TypeScale.Label);
        done.LocalPressed += (_, _) => CloseMenus();
        ui.Style.MinWidth = 0f;
        ui.NestOut();

        ui.NestOut(); // the panel
        slot.ActiveSelf = false;
        _settingsPanel = slot;
        ShowSettingsTab(0);
    }

    /// <summary>The "Start on" dropdown: every lens the sidebar offers, two columns, opened under its row.</summary>
    private void BuildStartLensMenu(UIBuilder ui)
    {
        var slot = BeginFloating(ui, "StartLensMenu", 0.34f, 0.74f, 0.5f, 1f, 6f, 10f);
        ui.Style.MinHeight = 7f * 46f;
        var area = ui.Empty("Choices");
        var grid = area.AttachComponent<GridLayout>();
        grid.ChildAlignment = Alignment.TopLeft;
        grid.CellSize.Value = new float2(200f, 40f);
        grid.Spacing.Value = new float2(8f, 6f);
        ui.NestInto(area.GetComponent<RectTransform>());
        for (var i = 0; i < MaxStartLensRows; i++)
        {
            var button = ui.Button((LocaleString)"");
            var label = button.Slot.GetComponentInChildren<Text>();
            Fix(label, TypeScale.Body);
            var index = i;
            button.LocalPressed += (_, _) =>
            {
                if (index < _startLensChoices.Count)
                {
                    _startLensMenu.ActiveSelf = false;
                    _actions.SetStartLens(_startLensChoices[index].Lens);
                }
            };
            button.Slot.ActiveSelf = false;
            _startLensItems.Add((button, label));
        }

        ui.NestOut();
        ui.NestOut();
        slot.ActiveSelf = false;
        _startLensMenu = slot;
        _menuAnchors[slot] = _startLensRow;
    }

    private void ToggleStartLensMenu()
    {
        if (_startLensMenu.ActiveSelf)
        {
            _startLensMenu.ActiveSelf = false;
            return;
        }

        for (var i = 0; i < _startLensItems.Count; i++)
        {
            var show = i < _startLensChoices.Count;
            _startLensItems[i].Button.Slot.ActiveSelf = show;
            if (show)
            {
                var (_, label, selected) = _startLensChoices[i];
                _startLensItems[i].Label.Content.Value = "<nobr>" + (selected ? "✓  " : "") + Plain(label) + "</nobr>";
                SetColor(_startLensItems[i].Button, selected ? Theme.ButtonSelected : Theme.Button);
            }
        }

        PositionMenu(_startLensMenu);
        _startLensMenu.ActiveSelf = true;
        AnimateOpen(_startLensMenu, MenuDrop);
    }

    /// <summary>One settings page: a vertical list that fills the page area.</summary>
    private Slot SettingsPage(UIBuilder ui, Action fill)
    {
        var layout = ui.VerticalLayout(6f, 0f, Alignment.TopCenter);
        layout.ForceExpandHeight.Value = false;
        var page = layout.Slot;
        fill();
        ui.NestOut();
        page.ActiveSelf = false;
        return page;
    }

    private void ShowSettingsTab(int index)
    {
        _settingsTab = index;
        for (var i = 0; i < _settingsPages.Count; i++)
        {
            _settingsPages[i].ActiveSelf = i == index;
            SetColor(_settingsTabs[i], i == index ? Theme.ButtonSelected : Theme.Button);
        }
    }

    private static void SettingsGroup(UIBuilder ui, string stringId)
    {
        ui.Style.MinHeight = 34f;
        var heading = ui.Text((LocaleString)UiStrings.Get(stringId).ToUpperInvariant(), bestFit: true, alignment: Alignment.BottomLeft);
        Fix(heading, TypeScale.Caption);
        heading.Color.Value = Theme.TextSecondary;
    }

    private static void SettingsLabel(UIBuilder ui, string text)
    {
        ui.Style.MinHeight = 30f;
        var label = ui.Text((LocaleString)text, bestFit: true, alignment: Alignment.BottomLeft);
        Fix(label, TypeScale.Body);
    }

    private (Image Track, RectTransform Knob) SettingsSwitch(UIBuilder ui, string text, Action pressed)
    {
        ui.Style.MinHeight = 48f;
        var row = SettingsSwitchRow(ui, text, pressed);
        return (row.Track, row.Knob);
    }

    private (Button Button, Text Label, Image Track, RectTransform Knob) SettingsSwitchRow(UIBuilder ui, string text, Action pressed)
    {
        var button = SwitchButton(ui, text, out var label, out var track, out var knob);
        button.LocalPressed += (_, _) => pressed();
        return (button, label, track, knob);
    }

    /// <summary>A "label ... value" row; pressing it steps to the next value.</summary>
    private Button ValueRow(UIBuilder ui, string text, Action pressed, out Text value)
    {
        ui.Style.MinHeight = 48f;
        var button = ui.Button((LocaleString)text);
        var label = button.Slot.GetComponentInChildren<Text>();
        Fix(label, TypeScale.Body);
        label.HorizontalAlign.Value = TextHorizontalAlignment.Left;
        if (label.Slot.GetComponent<RectTransform>() is { } labelRect)
        {
            labelRect.OffsetMin.Value = new float2(16f, 0f);
        }

        var inner = new UIBuilder(button.Slot.GetComponent<RectTransform>());
        RadiantUI_Constants.SetupDefaultStyle(inner);
        value = inner.Text((LocaleString)"", bestFit: true, alignment: Alignment.MiddleRight);
        Fix(value, TypeScale.Body);
        value.Color.Value = Theme.TextSecondary;
        value.Slot.GetComponent<RectTransform>()?.AddFixedPadding(16f);
        button.LocalPressed += (_, _) => pressed();
        return button;
    }

    public void ShowSettings(SettingsModel model)
    {
        var settings = model.Settings;
        _settingsListView = settings.ListView;

        ApplySwitch(_animationsSwitch.Track, _animationsSwitch.Knob, model.Animations);
        ApplySwitch(_liveOnlySwitch.Track, _liveOnlySwitch.Knob, model.LiveOnly);
        ApplySwitch(_notifyLiveSwitch.Track, _notifyLiveSwitch.Knob, settings.NotifyFavoriteLive);
        ApplySwitch(_notifyContactsSwitch.Track, _notifyContactsSwitch.Knob, settings.NotifyContactsInFavorite);
        _startLensValue.Content.Value = "<nobr>" + Plain(model.StartLens) + "  ▾</nobr>";
        _startLensChoices = model.StartLensChoices ?? [];

        SetColor(_viewGrid, settings.ListView ? Theme.Button : Theme.ButtonSelected);
        SetColor(_viewList, settings.ListView ? Theme.ButtonSelected : Theme.Button);
        var perRow = settings.CardsPerRow > 0 ? settings.CardsPerRow : 4;
        foreach (var (count, button) in _cardsPerRowButtons)
        {
            SetColor(button, count == perRow ? Theme.ButtonSelected : Theme.Button);
        }

        _hiddenItems = model.HiddenItems ?? [];
        for (var h = 0; h < _hiddenRows.Count; h++)
        {
            var show = h < _hiddenItems.Count;
            _hiddenRows[h].Row.ActiveSelf = show;
            if (show)
            {
                var item = _hiddenItems[h];
                _hiddenRows[h].Label.Content.Value = "<nobr><size=70%><alpha=#99>" + UiStrings.Get("settings.hidden." + item.Kind.ToString().ToLowerInvariant()) + "</size>   "
                    + Plain(CardText.Ellipsize(item.Name, 34)) + "</nobr>";
            }
        }

        _hiddenMore.Slot.ActiveSelf = _hiddenItems.Count > MaxHiddenRows;
        _hiddenMore.Content.Value = _hiddenItems.Count > MaxHiddenRows ? UiStrings.Format("settings.hidden.more", _hiddenItems.Count - MaxHiddenRows) : "";
        _hiddenCounts.Content.Value = UiStrings.Format("settings.hiddenCounts", settings.HiddenWorlds.Count, settings.HiddenAuthors.Count, settings.HiddenWords.Count);

        foreach (var (section, row) in _sectionRows)
        {
            var on = settings.IsSectionOn(section);
            row.Button.Slot.GetComponentInChildren<Text>().Content.Value = "<nobr><b>" + SectionName(section) + "</b></nobr>";
            ApplySwitch(row.Track, row.Knob, on);
            SetColor(row.Button, on ? Theme.SectionRow : Theme.ButtonQuiet);
        }

        foreach (var (lens, row) in _lensRows)
        {
            var switchOn = !settings.HiddenLenses.Contains(lens);
            var shown = settings.IsVisible(lens);
            row.Button.Slot.GetComponentInChildren<Text>().Content.Value = "<nobr>" + Plain(LensName(lens)) + "</nobr>";
            ApplySwitch(row.Track, row.Knob, switchOn);
            SetColor(row.Button, shown ? Theme.Button : Theme.ButtonQuiet);
        }

        if (!_settingsPanel.ActiveSelf)
        {
            CloseMenus();
            _settingsScrim.ActiveSelf = true;
            _settingsPanel.ActiveSelf = true;
            AnimateOpen(_settingsPanel, ModalRise);
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

        var slot = BeginFloating(ui, "Prompt", 0.28f, 0.72f, 0.5f, 0.5f, 12f, 22f);

        ui.Style.MinHeight = 52f;
        _promptTitle = ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.MiddleCenter);
        Fix(_promptTitle, TypeScale.Title);
        _promptTitle.Color.Value = Theme.Heading;

        ui.Style.MinHeight = 56f;
        _promptField = PromptField(ui, "prompt.listPlaceholder");
        _promptField.Editor.Target.LocalSubmitPressed += _ => SubmitPrompt();

        ui.Style.MinHeight = 52f;
        var row = ui.HorizontalLayout(10f, 0f, Alignment.MiddleCenter);
        var cancel = ui.Button((LocaleString)UiStrings.Get("prompt.cancel"), Theme.ButtonQuiet);
        Fix(cancel.Slot.GetComponentInChildren<Text>(), TypeScale.Body);
        cancel.LocalPressed += (_, _) => ClosePrompt();
        var ok = ui.Button((LocaleString)UiStrings.Get("prompt.ok"), Theme.ButtonSelected);
        Fix(ok.Slot.GetComponentInChildren<Text>(), TypeScale.Label);
        ok.LocalPressed += (_, _) => SubmitPrompt();
        ui.NestOut();

        ui.NestOut(); // the panel
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
        AnimateOpen(_promptPanel, ModalRise);
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
            SetColor(button, item.Selected ? Theme.ButtonSelected : Theme.Button);
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

    /// <summary>
    /// Shows the chips in use first (the controller orders them), then as many of the rest as fit the bar's width, so
    /// the row is never cut off. Chip widths are the ones set below (text length based); the bar's width comes from its
    /// computed rect.
    /// </summary>
    private void RenderChips(IReadOnlyList<ChipView> chips)
    {
        var barWidth = RectField?.GetValue(_chipBarRect) is Rect bar && bar.size.x > 200f ? bar.size.x : 1000f;
        var anyActive = chips.Any(c => c.State is not null);
        var room = barWidth - 24f - (anyActive ? 130f : 0f); // padding, and the Clear button when it shows
        var used = 0f;
        for (var i = 0; i < MaxChips; i++)
        {
            var (button, label) = _chips[i];
            var fits = false;
            if (i < chips.Count)
            {
                var chip = chips[i];
                var mark = chip.State switch { ChipState.Required => "+ ", ChipState.Excluded => "− ", _ => "" };
                var width = Math.Clamp((mark.Length + chip.Label.Length) * 11f + 34f, 96f, 240f);
                fits = chip.State is not null || used + width + 8f <= room;
                if (fits)
                {
                    used += width + 8f;
                    _chipTags[i] = chip.Tag;
                    label.Content.Value = mark + chip.Label;
                    if (button.Slot.GetComponent<LayoutElement>() is { } chipLayout)
                    {
                        // Wide enough for its text, so a long tag ("Educational") is not squeezed into a smaller font.
                        chipLayout.MinWidth.Value = width;
                    }

                    SetColor(button, chip.State == ChipState.Excluded ? Theme.ChipExcluded
                        : chip.Personal ? Theme.ChipPersonal
                        : chip.State == ChipState.Required ? Theme.ChipRequired
                        : Theme.Button);
                }
            }

            if (!fits)
            {
                _chipTags[i] = null;
            }

            button.Slot.ActiveSelf = fits;
        }

        _clearChips.Slot.ActiveSelf = anyActive;
    }

    private void RenderSortMenu(BrowseView view)
    {
        foreach (var item in view.SortMenu)
        {
            var (button, label) = _sortItems[item.Key];
            label.Content.Value = "<nobr>" + (item.Selected ? "✓  " : "") + item.Label + "   <size=65%>" + item.Hint + "</size></nobr>";
            button.Enabled = item.Enabled;
            button.Slot.ActiveSelf = item.Enabled; // sorts this tab cannot use are left out, not greyed
            SetColor(button, item.Selected ? Theme.ButtonSelected : Theme.Button);
        }

        _sortDirectionRow.ActiveSelf = !view.SortMenu.Any(i => i.Selected && i.Key == SortKey.Random);

        SetColor(_descending, view.Descending ? Theme.ButtonSelected : Theme.Button);
        SetColor(_ascending, view.Descending ? Theme.Button : Theme.ButtonSelected);
    }

    private void RenderFiltersMenu(BrowseView view)
    {
        var f = view.Filters;
        var source = LensEngine.SourceOf(view.Lens);
        var sessions = source == LensSource.Sessions;
        var search = source == LensSource.Search;
        foreach (var slot in _filtersForSessions)
        {
            slot.ActiveSelf = sessions;
        }

        foreach (var slot in _filtersForSearch)
        {
            slot.ActiveSelf = search;
        }

        _filtersNone.Slot.ActiveSelf = !sessions && !search;

        Toggle(FilterOp.ToggleHasRoom, f.HasRoom);
        Toggle(FilterOp.ToggleMobile, f.MobileFriendly);
        Toggle(FilterOp.ToggleVr, f.HasVr);
        Toggle(FilterOp.ToggleOtherVersions, view.ShowOtherVersions);
        Toggle(FilterOp.ToggleFeatured, f.FeaturedOnly);
        Toggle(FilterOp.ToggleIncludeEmpty, f.IncludeEmpty);

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
        }
    }

    private void Toggle(FilterOp op, bool on)
    {
        var (track, knob) = _switches[op];
        ApplySwitch(track, knob, on);
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
        // Line-based alignment centres the line, not the glyphs: with the default (geometric) one a word with a
        // descender ("hangout", "Settings") sits higher in its box than a word without ("Refresh").
        text.AlignmentMode.Value = AlignmentMode.LineBased;
        text.AutoSizeMin.Value = min;
        text.AutoSizeMax.Value = max;
    }

    /// <summary>One fixed size from <see cref="TypeScale"/>: no shrinking to fit, so equal kinds of text look equal.</summary>
    internal static void Fix(Text text, float size)
    {
        text.AlignmentMode.Value = AlignmentMode.LineBased;
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
        DiscoverUi.ApplyTextLift(slot);
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

    /// <summary>Row text is larger than grid text (a row has the room), and the buttons and tags read at the same size.</summary>
    private void SetTextSizes(bool list)
    {
        void Range(Text text, float gridMin, float gridMax, float listMin, float listMax) =>
            DiscoverUi.Clamp(text, list ? listMin : gridMin, list ? listMax : gridMax);

        Range(_title, 18f, 26f, 22f, 28f);
        Range(_meta, 14f, 18f, 15f, 18f);
        Range(_footer, 13f, 17f, 15f, 18f);
        Range(_starLabel, 16f, 28f, 20f, 28f);
        Range(_buttonLabel, 13f, 17f, 15f, 18f);
        Range(_info.Slot.GetComponentInChildren<Text>(), 12f, 18f, 15f, 18f);
        Range(_organize.Slot.GetComponentInChildren<Text>(), 12f, 18f, 15f, 18f);
        foreach (var (_, label) in _tags)
        {
            Range(label, 12f, 16f, 15f, 18f);
        }
    }

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

        // The press and hover target: the whole card in the grid; only the picture in a row, so the row's own buttons
        // stay reachable and hovering the picture still pans it.
        _catcher.ActiveSelf = true;
        var catcherRect = _catcher.GetComponent<RectTransform>();
        catcherRect.AnchorMin.Value = float2.Zero;
        catcherRect.AnchorMax.Value = list ? new float2(0.18f, 1f) : float2.One;
        SetTextSizes(list);
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
        var button = ui.Button((LocaleString)text, Theme.CardButton);
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
            if (show && _tags[i].Button.Slot.GetComponent<LayoutElement>() is { } tagLayout)
            {
                tagLayout.MinWidth.Value = Math.Clamp(card.Tags[i].Text.Length * (_list ? 10f : 8f) + 26f, 60f, 200f);
            }

            _tagPersonal[i] = show && card.Tags[i].Personal;
            if (show)
            {
                _tags[i].Button.ColorDrivers[0].SetColors(_tagPersonal[i] ? Theme.ChipPersonal : Theme.CardButton);
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
