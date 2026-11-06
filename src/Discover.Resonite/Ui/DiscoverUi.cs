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
internal sealed partial class DiscoverUi
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
    private readonly Dictionary<SortKey, Text> _sortHints = [];
    private readonly Dictionary<FilterOp, (Button Button, Text Label)> _filterItems = [];

    private Slot _content = null!;
    private Text _title = null!;
    private Text _subtitle = null!;
    private LensKind? _headerLens;
    private Text _count = null!;
    private Text _status = null!;
    private Text _emptyText = null!;
    private Slot _emptyArea = null!;
    private Slot _clearFiltersButton = null!;
    private Slot _showHiddenButton = null!;
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
    private Slot _scanLine = null!;
    private RectTransform _scanLineRect = null!;
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

            // A label that fills its button (no placement of its own) is inset a little, so long words do not run to the edge.
            if (text.Slot.Parent?.GetComponent<Button>() is not null && rect.OffsetMin.Value.x == 0f && rect.OffsetMax.Value.x == 0f)
            {
                rect.OffsetMin.Value += new float2(10f, 0f);
                rect.OffsetMax.Value -= new float2(10f, 0f);
            }

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

    /// <summary>Shows or hides a rail row's accent bar; a bar that has just become visible fades in.</summary>
    private static void ShowAccentBar(Slot bar, bool selected)
    {
        var wasShown = bar.ActiveSelf;
        bar.ActiveSelf = selected;
        if (selected && !wasShown && bar.GetComponent<Image>() is { } image)
        {
            UiMotion.FadeIn(image.Tint, Theme.Text, 0.22f);
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

        // "Tags" at the left, then the chips inside an outlined box, so the row reads as a tag selection.
        var title = ui.Text((LocaleString)UiStrings.Get("chips.title"), bestFit: true, alignment: Alignment.MiddleLeft);
        Fix(title, TypeScale.Label);
        title.Color.Value = Theme.TextSecondary;
        title.RectTransform.AnchorMin.Value = float2.Zero;
        title.RectTransform.AnchorMax.Value = new float2(0f, 1f);
        title.RectTransform.OffsetMin.Value = new float2(10f, 0f);
        title.RectTransform.OffsetMax.Value = new float2(ChipTitleWidth, 0f);

        var outline = ui.Panel(RadiantUI_Constants.Neutrals.MIDLIGHT, _rounded, NineSliceSizing.FixedSize);
        outline.Slot.Name = "TagBox";
        outline.RectTransform.AnchorMin.Value = float2.Zero;
        outline.RectTransform.AnchorMax.Value = float2.One;
        outline.RectTransform.OffsetMin.Value = new float2(ChipTitleWidth + 4f, 0f);
        outline.RectTransform.OffsetMax.Value = float2.Zero;
        var inner = ui.Panel(Theme.Rail, _rounded, NineSliceSizing.FixedSize);
        inner.RectTransform.OffsetMin.Value = new float2(2f, 2f);
        inner.RectTransform.OffsetMax.Value = new float2(-2f, -2f);

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
        ui.NestOut(); // the inner panel
        ui.NestOut(); // the outline
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
    private const float ChipTitleWidth = 80f;

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
        ui.HorizontalLayout(10f, 0f, Alignment.MiddleCenter);
        var showHidden = ui.Button((LocaleString)UiStrings.Get("empty.showHidden"), Theme.ButtonSelected);
        showHidden.LocalPressed += (_, _) => _actions.ShowHiddenByFilters();
        _showHiddenButton = showHidden.Slot;
        var clear = ui.Button((LocaleString)UiStrings.Get("empty.clearFilters"), Theme.ButtonSelected);
        clear.LocalPressed += (_, _) => _actions.ClearChips();
        _clearFiltersButton = clear.Slot;
        ui.NestOut(); // the buttons
        ui.NestOut();
        ui.NestOut();
        _emptyArea.ActiveSelf = false;

        // The tab-switch scan line: a thin cyan line over the results area (last in it, so nothing is drawn over it). It
        // travels with the results viewport's bottom edge while that edge moves (see Cover and Reveal).
        var line = ui.Image(Theme.Tab);
        line.Slot.Name = "ScanLine";
        line.InteractionTarget.Value = false;
        line.RectTransform.AnchorMin.Value = float2.Zero;
        line.RectTransform.AnchorMax.Value = new float2(1f, 0f);
        line.RectTransform.OffsetMin.Value = new float2(8f, -2f);
        line.RectTransform.OffsetMax.Value = new float2(-8f, 2f);
        line.Slot.ActiveSelf = false;
        _scanLine = line.Slot;
        _scanLineRect = line.RectTransform;
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

                var created = CardView.Create(_content, _screen.World, _rounded, _actions, PanTuning);
                created.SetListLayout(_listLayout);
                _cards.Add(created);
                creates++;
            }

            try
            {
                _cards[_pendingIndex].Bind(_pendingCards[_pendingIndex], _screen.Engine);
            }
            catch (Exception ex)
            {
                // One bad card must not stop the ones after it from being filled in (they would stay grey and dead).
                UniLog.Warning($"[Discover] Card bind failed: {ex.GetType().Name}");
                _cards[_pendingIndex].Release();
            }

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
            ShowAccentBar(extras.Bar, item.Selected);
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
                var name = RichText.Neutralize(CardText.Ellipsize(item.Label, 18));
                label.Content.Value = "<nobr>" + name + "</nobr>";
                _listExtras[i].Count.Content.Value = item.Count is null ? "" : item.Count.ToString()!;
                ShowAccentBar(_listExtras[i].Bar, item.Selected);
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
        _title.Content.Value = "<nobr>" + RichText.Neutralize(view.Title) + "</nobr>";
        if (view.Lens != _headerLens)
        {
            _headerLens = view.Lens;
            UiMotion.FadeIn(_title.Color, Theme.Heading);
            UiMotion.FadeIn(_subtitle.Color, Theme.TextSecondary, 0.36f);
        }
        _subtitle.Content.Value = "<nobr>" + RichText.Neutralize(view.Subtitle) + "</nobr>";
        _count.Content.Value = view.Count;
        _sortButtonLabel.Content.Value = $"<nobr>{UiStrings.Get("bar.sort")}: {view.SortLabel} ▾</nobr>";
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
                _cards[i].Stale = true; // shows the old world until it is rebound; a press on it must not open that world
            }
        }

        PumpCards();

        var empty = view.Cards.Count == 0 && view.EmptyMessage is not null;
        _emptyArea.ActiveSelf = empty;
        if (empty)
        {
            _emptyText.Content.Value = view.EmptyHint is null ? view.EmptyMessage! : $"{view.EmptyMessage}\n<size=60%>{view.EmptyHint}</size>";
            // One button: the hidden worlds' own when there are any, else the plain clear.
            _showHiddenButton.ActiveSelf = view.HiddenByFilters > 0;
            _clearFiltersButton.ActiveSelf = view.ShowClearFilters && view.HiddenByFilters == 0;
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
        var room = barWidth - 24f - ChipTitleWidth - (anyActive ? 130f : 0f); // padding, and the Clear button when it shows
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
                    label.Content.Value = mark + RichText.Neutralize(chip.Label);
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
            label.Content.Value = "<nobr>" + (item.Selected ? "✓  " : "") + item.Label + "</nobr>";
            _sortHints[item.Key].Content.Value = "<nobr>" + item.Hint + "</nobr>";
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
    /// <summary>
    /// A dark sheet with a cyan line along its top edge, filling its parent and hidden. Its AnchorMax y is the height it
    /// covers from the bottom, so tweening it moves the line (see <see cref="Cover"/>, <see cref="Reveal"/>).
    /// </summary>
    private (Slot Sheet, RectTransform Rect) SweepSheet(UIBuilder ui)
    {
        var sheet = ui.Image(Theme.Rail);
        sheet.Sprite.Target = _rounded;
        sheet.NineSliceSizing.Value = NineSliceSizing.FixedSize;
        sheet.Slot.Name = "Sweep";
        sheet.InteractionTarget.Value = false;
        var edge = new UIBuilder(sheet.RectTransform);
        RadiantUI_Constants.SetupDefaultStyle(edge);
        var line = edge.Image(Theme.Tab);
        line.Slot.Name = "Edge";
        line.InteractionTarget.Value = false;
        line.RectTransform.AnchorMin.Value = new float2(0f, 1f);
        line.RectTransform.AnchorMax.Value = new float2(1f, 1f);
        line.RectTransform.OffsetMin.Value = new float2(0f, -4f);
        line.RectTransform.OffsetMax.Value = float2.Zero;
        sheet.Slot.ActiveSelf = false;
        return (sheet.Slot, sheet.RectTransform);
    }

    /// <summary>
    /// The old cards are scanned away: the results viewport's bottom edge rises from the bottom to the top (the viewport
    /// clips what is below it, so the cards vanish at the line and the pane's own background shows), then
    /// <paramref name="onCovered"/> runs on the update thread.
    /// </summary>
    public void Cover(float seconds, Action onCovered)
    {
        var generation = ++_curtainGeneration;
        _scanLine.ActiveSelf = true;
        Sweep(0f, 1f, seconds, CurvePreset.QuarticInOut, () =>
        {
            if (generation == _curtainGeneration)
            {
                onCovered();
            }
        });
    }

    /// <summary>The new cards are scanned in: the bottom edge travels back down, uncovering them from the top. A no-op when nothing is scanned away.</summary>
    public void Reveal(float seconds, float rise)
    {
        if (!_scanLine.ActiveSelf)
        {
            return;
        }

        var generation = ++_curtainGeneration;
        Sweep(1f, 0f, seconds, CurvePreset.QuarticOut, () =>
        {
            if (generation == _curtainGeneration)
            {
                _scanLine.ActiveSelf = false;
            }
        });
    }

    /// <summary>Moves the viewport's bottom edge and the scan line together, from <paramref name="from"/> to <paramref name="to"/> (fractions of the area's height).</summary>
    private void Sweep(float from, float to, float seconds, CurvePreset curve, Action done)
    {
        _viewport.AnchorMin.TweenFromTo(new float2(0f, from), new float2(0f, to), seconds, curve, onDoneLocal: () => done());
        _scanLineRect.AnchorMin.TweenFromTo(new float2(0f, from), new float2(0f, to), seconds, curve);
        _scanLineRect.AnchorMax.TweenFromTo(new float2(1f, from), new float2(1f, to), seconds, curve);
    }

    /// <summary>Ends any transition at once: the results fully shown, the line hidden.</summary>
    public void Settle()
    {
        _curtainGeneration++;
        _viewport.AnchorMin.ClearTweens();
        _scanLineRect.AnchorMin.ClearTweens();
        _scanLineRect.AnchorMax.ClearTweens();
        _viewport.AnchorMin.Value = float2.Zero;
        _scanLine.ActiveSelf = false;
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
