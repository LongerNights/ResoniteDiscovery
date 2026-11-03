using Discover.Core.Lenses;
using Discover.Core.Model;
using Discover.Core.Presentation;
using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.UIX;

namespace Discover.Resonite.Ui;

/// <summary>
/// The Galaxy Sieve screen: lens rail on the left; a header with the lens title and counts; a scrolling card grid
/// with a centred empty state (spec "UI"). Built once, the first time the screen is shown. M2 binds the first
/// <see cref="MaxCards"/> entries into pooled card slots; row recycling and paging arrive with M3.
/// Text comes from <see cref="UiStrings"/>, colours from <see cref="Theme"/>.
///
/// UIBuilder rules that matter here (verification.md): <c>Panel</c>, layouts and <c>Button</c> nest by themselves;
/// split gaps are fractions; <c>HorizontalHeader</c> is a top strip.
/// </summary>
internal sealed class DiscoverUi
{
    private static readonly float2 CellSize = new(360f, 250f);
    private static readonly float2 Spacing = new(16f, 16f);

    private readonly RadiantDashScreen _screen;
    private readonly Services.JoinService _join;
    private readonly Action<LensKind> _onLens;
    private readonly Action<string> _onJoin;
    private readonly Dictionary<LensKind, Button> _railButtons = [];
    private readonly List<CardView> _cards = [];
    private Slot _content = null!;
    private Text _title = null!;
    private Text _count = null!;
    private Text _empty = null!;
    private Image _curtain = null!;
    private RectTransform _viewport = null!;
    private float2 _viewportMin;
    private float2 _viewportMax;

    public DiscoverUi(RadiantDashScreen screen, Services.JoinService join, int maxCards, Action<LensKind> onLens, Action<string> onJoin)
    {
        _screen = screen;
        _join = join;
        MaxCards = maxCards;
        _onLens = onLens;
        _onJoin = onJoin;
    }

    public int MaxCards { get; }

    public void Build()
    {
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
        var layout = ui.VerticalLayout(10f, 16f, Alignment.TopCenter); // Panel and VerticalLayout each nest
        layout.ForceExpandHeight.Value = false;

        ui.Style.MinHeight = 72f;
        var title = ui.Text((LocaleString)UiStrings.Get("screen.title"), bestFit: true, alignment: Alignment.MiddleCenter);
        Clamp(title, 28f, 44f);
        title.Color.Value = Theme.Heading;

        ui.Style.MinHeight = 36f;
        var section = ui.Text((LocaleString)UiStrings.Get("rail.live"), bestFit: true, alignment: Alignment.BottomLeft);
        Clamp(section, 16f, 22f);
        section.Color.Value = Theme.TextSecondary;

        ui.Style.MinHeight = 64f;
        AddLens(ui, LensKind.LiveNow, "lens.liveNow");
        AddLens(ui, LensKind.Friends, "lens.friends");
        ui.NestOut();
        ui.NestOut();
        ui.NestOut();
    }

    private void AddLens(UIBuilder ui, LensKind lens, string stringId)
    {
        var button = ui.Button((LocaleString)UiStrings.Get(stringId));
        button.LocalPressed += (_, _) => _onLens(lens);
        _railButtons[lens] = button;
    }

    private void BuildMain(UIBuilder ui, RectTransform main)
    {
        ui.NestInto(main);
        ui.HorizontalHeader(88f, out var header, out var body); // a top strip; VerticalHeader would be a left column

        header.AddFixedPadding(12f, 24f, 4f, 24f);
        ui.NestInto(header);
        ui.SplitHorizontally(0.55f, out var titleRect, out var countRect);
        _title = TextIn(ui, titleRect, Alignment.MiddleLeft, Theme.Heading, 30f, 48f);
        _count = TextIn(ui, countRect, Alignment.MiddleRight, Theme.TextSecondary, 20f, 30f);
        ui.NestOut();

        ui.NestInto(body);
        var viewport = ui.Next("Results");
        ScrollRect.CreateScrollRect(viewport, out var content);
        _viewport = viewport.GetComponent<RectTransform>();
        _viewport.AddFixedPadding(8f, 16f, 8f, 16f);
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

        // The empty state sits over the grid, centred, and is hidden while there are cards.
        _empty = ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.MiddleCenter);
        Clamp(_empty, 20f, 32f);
        _empty.Color.Value = Theme.TextSecondary;
        _empty.RectTransform.AddFixedPadding(48f);
        _empty.Slot.ActiveSelf = false;
        ui.NestOut();

        // Transition curtain: the last child of the main pane, so it draws over the header and grid. It never takes
        // pointer hits and is inactive whenever it is fully transparent.
        _curtain = ui.Image(Theme.CurtainHidden);
        _curtain.InteractionTarget.Value = false;
        _curtain.Slot.ActiveSelf = false;
        ui.NestOut();
    }

    // Tween<T> also calls its done-callback when it is destroyed early, and TweenTo clears (destroys) the field's
    // previous tween. Each animation therefore takes a generation number, and a callback acts only if it is still
    // the newest one; otherwise a second click mid-transition would run the old swap or hide the new curtain.
    private int _curtainGeneration;

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

    /// <summary>Header text: lens title on the left, counts on the right; <paramref name="empty"/> shows over the grid.</summary>
    public void SetHeader(string title, string count, string? empty)
    {
        _title.Content.Value = title;
        _count.Content.Value = count;
        _empty.Content.Value = empty ?? "";
        _empty.Slot.ActiveSelf = empty is not null;
    }

    /// <summary>Lights the active lens; with <paramref name="fadeSeconds"/> above 0 the colour change is eased.</summary>
    public void HighlightLens(LensKind active, float fadeSeconds = 0f)
    {
        foreach (var (lens, button) in _railButtons)
        {
            // The button's colour driver owns the visible tint; writing BaseColor alone is overridden.
            var driver = button.ColorDrivers[0];
            var from = driver.NormalColor.Value;
            var to = lens == active ? Theme.ButtonSelected : Theme.Button;
            driver.SetColors(to); // also sets the hover and press colours
            if (fadeSeconds > 0f && from != to)
            {
                driver.NormalColor.TweenFromTo(from, to, fadeSeconds, CurvePreset.SineInOut);
            }
        }
    }

    /// <summary>Binds entries to pooled cards (creating up to <see cref="MaxCards"/>), deactivating the rest.</summary>
    public void ShowEntries(IReadOnlyList<DiscoveryEntry> entries, DateTime nowUtc)
    {
        var shown = Math.Min(entries.Count, MaxCards);
        for (var i = 0; i < shown; i++)
        {
            if (i >= _cards.Count)
            {
                _cards.Add(CardView.Create(_content, _screen.World, _join, _onJoin));
            }

            _cards[i].Bind(entries[i], nowUtc, _screen.Engine);
        }

        for (var i = shown; i < _cards.Count; i++)
        {
            _cards[i].Release();
        }
    }

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
/// One pooled card (spec "Card anatomy"): thumbnail with a users pill (bottom left) and an Open pill (top right);
/// one-line title; host and age; badges and the Join button. Device mix and tag chips belong to the drawer (M4).
/// </summary>
internal sealed class CardView
{
    private const int TitleChars = 34;
    private const int HostChars = 22;

    private readonly Slot _root;
    private readonly Services.JoinService _join;
    private Text _title = null!;
    private Text _sub = null!;
    private Text _users = null!;
    private Text _badges = null!;
    private Slot _usersPill = null!;
    private Slot _openPill = null!;
    private Button _button = null!;
    private Text _buttonLabel = null!;
    private StaticTexture2D _thumbnail = null!;
    private Uri? _thumbnailUri;

    private CardView(Slot root, Services.JoinService join)
    {
        _root = root;
        _join = join;
    }

    public string? Key { get; private set; }

    public static CardView Create(Slot parent, World world, Services.JoinService join, Action<string> onJoin)
    {
        var slot = parent.AddSlot("Card");
        slot.PersistentSelf = false;
        var card = new CardView(slot, join);
        var rounded = RadiantUI_Constants.GetButtonSprite(world);

        var ui = new UIBuilder(slot.AttachComponent<RectTransform>());
        RadiantUI_Constants.SetupDefaultStyle(ui);
        ui.Panel(Theme.Card, rounded, NineSliceSizing.FixedSize); // rounded card; Panel nests

        var body = ui.Next("Body");
        body.GetComponent<RectTransform>().AddFixedPadding(8f);
        ui.Nest();

        // thumbnail, gap, title, host and age, badges with the button
        var parts = ui.SplitVertically(0.55f, 0.03f, 0.14f, 0.11f, 0.17f);

        // Thumbnail: world thumbnails are 360 degree images, drawn the way the stock card draws them (verification V10).
        var texture = parts[0].Slot.AttachComponent<StaticTexture2D>();
        var block = parts[0].Slot.AttachComponent<Projection360PropertyBlock>();
        block.Texture.Target = texture;
        block.PerspectiveFieldOfView.Value = float2.One * 110f;
        ui.NestInto(parts[0]);
        ui.RawGraphic(LegacyWorldThumbnailItem.GetWorldPreviewMaterial(world), block);
        card._thumbnail = texture;
        card._usersPill = Pill(ui, rounded, Theme.Pill, new float2(0.03f, 0.06f), new float2(0.34f, 0.28f), out card._users);
        card._openPill = Pill(ui, rounded, Theme.ButtonSelected, new float2(0.70f, 0.72f), new float2(0.97f, 0.94f), out var openText);
        openText.Content.Value = UiStrings.Get("card.open");
        ui.NestOut();

        card._title = DiscoverUi.TextIn(ui, parts[2], Alignment.MiddleLeft, Theme.Text, 18f, 28f);
        card._sub = DiscoverUi.TextIn(ui, parts[3], Alignment.MiddleLeft, Theme.TextSecondary, 14f, 20f);

        ui.NestInto(parts[4]);
        ui.SplitHorizontally(0.62f, out var badgeRect, out var buttonRect, 0.03f);
        card._badges = DiscoverUi.TextIn(ui, badgeRect, Alignment.MiddleLeft, Theme.Text, 14f, 20f);
        ui.NestInto(buttonRect);
        card._button = ui.Button((LocaleString)UiStrings.Get("card.join"), Theme.ButtonSelected);
        card._buttonLabel = card._button.Slot.GetComponentInChildren<Text>();
        DiscoverUi.Clamp(card._buttonLabel, 16f, 22f);
        card._button.LocalPressed += (_, _) =>
        {
            if (card.Key is not null)
            {
                onJoin(card.Key);
            }
        };
        ui.NestOut();
        ui.NestOut();
        return card;
    }

    /// <summary>A small rounded label laid over the thumbnail, anchored by fractions of the thumbnail's rect.</summary>
    private static Slot Pill(UIBuilder ui, IAssetProvider<Sprite> sprite, colorX color, float2 anchorMin, float2 anchorMax, out Text text)
    {
        var image = ui.Panel(color, sprite, NineSliceSizing.FixedSize);
        image.RectTransform.AnchorMin.Value = anchorMin;
        image.RectTransform.AnchorMax.Value = anchorMax;
        text = ui.Text((LocaleString)"", bestFit: true, alignment: Alignment.MiddleCenter);
        DiscoverUi.Clamp(text, 12f, 20f);
        text.Color.Value = Theme.Text;
        text.RectTransform.AddFixedPadding(2f, 6f, 2f, 6f);
        ui.NestOut();
        return image.Slot;
    }

    public void Bind(DiscoveryEntry entry, DateTime nowUtc, Engine engine)
    {
        Key = entry.Key;
        _root.ActiveSelf = true;

        var best = entry.BestSession ?? entry.Sessions.FirstOrDefault();
        var isOpen = best is not null && _join.IsOpen(best.SessionId);

        var name = entry.DisplayName.Length > 0 ? entry.DisplayName : UiStrings.Get("card.unnamed");
        _title.Content.Value = OneLine(CardText.Ellipsize(name, TitleChars));

        var sub = best is null ? "" : $"{Safe(CardText.Ellipsize(best.HostUsername, HostChars))} · {CardText.Age(best.SessionBeginTime, nowUtc)}";
        if (entry.Sessions.Count > 1)
        {
            sub += " · " + UiStrings.Format("card.sessions", entry.Sessions.Count);
        }

        _sub.Content.Value = "<nobr>" + sub + "</nobr>";

        _usersPill.ActiveSelf = best is not null;
        _users.Content.Value = best is null ? "" : CardText.Users(best.ActiveUsers, best.MaximumUsers);
        _openPill.ActiveSelf = isOpen;

        var badges = new List<string>(3);
        if (entry.FriendsPresent > 0)
        {
            badges.Add(Colored(RadiantUI_Constants.Hero.GREEN_HEX, "★ " + (entry.FriendsPresent == 1 ? UiStrings.Get("card.friend") : UiStrings.Format("card.friends", entry.FriendsPresent))));
        }

        if (entry.IsMobileFriendly)
        {
            badges.Add(Colored(RadiantUI_Constants.Hero.CYAN_HEX, UiStrings.Get("card.mobile")));
        }

        if (entry.IsFull)
        {
            badges.Add(Colored(RadiantUI_Constants.Hero.RED_HEX, UiStrings.Get("card.full")));
        }

        _badges.Content.Value = "<nobr>" + string.Join("  ", badges) + "</nobr>";

        // Join focuses an open world instead of joining twice (F8); it is disabled for a full world (F7).
        _buttonLabel.Content.Value = UiStrings.Get(isOpen ? "card.focus" : "card.join");
        _button.Enabled = isOpen || !entry.IsFull;

        SetThumbnail(entry, best, engine);
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

    private void SetThumbnail(DiscoveryEntry entry, SessionSnapshot? best, Engine engine)
    {
        var raw = best?.ThumbnailUrl ?? entry.ThumbnailUrl;
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

    /// <summary>Plain text shown literally (verification V11) and kept on one line.</summary>
    private static string OneLine(string value) => "<nobr>" + Safe(value) + "</nobr>";

    private static string Safe(string value) => StringParsingHelper.SanitizeFormatTags(value) ?? "";

    private static string Colored(string hex, string text) => $"<color={hex}>{text}</color>";
}
