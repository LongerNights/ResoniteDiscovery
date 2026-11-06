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
    private Button _leave = null!;
    private Text _leaveLabel = null!;
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

    /// <summary>True from the moment a new lens is queued until this card is rebound; presses are ignored meanwhile.</summary>
    public bool Stale { get; set; }

    public static CardView Create(Slot parent, World world, IAssetProvider<Sprite> rounded, IBrowseActions actions, PanTuning pan)
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
            if (card.Key is not null && !card.Stale)
            {
                actions.ToggleFavorite(card.Key);
            }
        };
        var info = OverlayButton(ui, UiStrings.Get("card.info"), new float2(0.50f, 0.74f), new float2(0.78f, 0.96f), out var infoLabel);
        card._info = info;
        DiscoverUi.Clamp(infoLabel, 12f, 18f);
        info.LocalPressed += (_, _) =>
        {
            if (card.Key is not null && !card.Stale)
            {
                DiscoverUi.PendingOrigin = card._root;
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
            if (card.Key is not null && !card.Stale)
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
            if (card.Key is not null && !card.Stale)
            {
                actions.CardPressed(card.Key);
            }
        };
        ui.NestOut();
        ui.NestOut();
        ui.NestOut();

        // The press target over the whole card (grid mode). Direct colours, all the same: no hover tint and no opaque grey (see Catcher).
        var (catcher, press) = Theme.Catcher(ui, "Open");
        press.LocalPressed += (_, _) =>
        {
            if (card.Key is not null && !card.Stale)
            {
                DiscoverUi.PendingOrigin = card._root;
                actions.OpenDetails(card.Key);
            }
        };
        card._catcher = catcher.Slot;

        // Quick leave for Current Worlds (a red x, top right), made after the catcher so it sits above it.
        var overlay = new UIBuilder(slot.GetComponent<RectTransform>());
        RadiantUI_Constants.SetupDefaultStyle(overlay);
        card._leave = overlay.Button((LocaleString)"✕", Theme.ClearButton);
        var leaveRect = card._leave.Slot.GetComponent<RectTransform>();
        PlaceLeave(leaveRect, list: false);
        card._leaveLabel = card._leave.Slot.GetComponentInChildren<Text>();
        DiscoverUi.Clamp(card._leaveLabel, 14f, 24f);

        // The glyph sits low in its box; raise it (the box keeps its size).
        if (card._leaveLabel.Slot.GetComponent<RectTransform>() is { } leaveLabelRect)
        {
            leaveLabelRect.OffsetMin.Value = new float2(0f, 6f);
            leaveLabelRect.OffsetMax.Value = new float2(0f, 6f);
        }

        card._leave.Slot.ActiveSelf = false;
        card._leave.LocalPressed += (_, _) =>
        {
            if (card.Key is not null && !card.Stale)
            {
                actions.LeaveWorld(card.Key);
            }
        };

        // Hovering pans the 360 preview, exactly as on the stock card: its panner reads hover events from this button.
        var panner = catcher.Slot.AttachComponent<Perspective360Panner>();
        pan.Register(panner);
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

        var leaveRect = _leave.Slot.GetComponent<RectTransform>();
        PlaceLeave(leaveRect, list);

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

    /// <summary>The leave x: a fixed square in the card's top right corner (grid), or the top right of the picture (list).</summary>
    private static void PlaceLeave(RectTransform rect, bool list)
    {
        // Same height as the picture's corner pill (about 49) and the same inset from the card's top and sides (measured
        // in game: the pill sits 19 from the left and 20 from the top), so the x lines up with the counter.
        const float size = 49f, insetX = 19f, insetY = 20f;
        if (list)
        {
            // The top right corner of the picture (its right edge is 17% of the row), as in the grid card.
            // Measured in game: the picture's right edge is about 10 right of that anchor and its top about 8 below the
            // card's, so these insets leave 9 from the picture's edges, the same as the counter pill's from its left.
            const float insetRight = 2f, insetTop = 17f;
            rect.AnchorMin.Value = new float2(0.17f, 1f);
            rect.AnchorMax.Value = new float2(0.17f, 1f);
            rect.OffsetMin.Value = new float2(-size - insetRight, -size - insetTop);
            rect.OffsetMax.Value = new float2(-insetRight, -insetTop);
        }
        else
        {
            rect.AnchorMin.Value = new float2(1f, 1f);
            rect.AnchorMax.Value = new float2(1f, 1f);
            rect.OffsetMin.Value = new float2(-size - insetX, -size - insetY);
            rect.OffsetMax.Value = new float2(-insetX, -insetY);
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
        Stale = false;
        _root.ActiveSelf = true;

        _title.Content.Value = RichText.Neutralize(card.Title);
        _title.Color.Value = card.Dimmed ? Theme.TextSecondary : Theme.Text;
        _meta.Content.Value = "<nobr>" + (card.Visited ? $"<color={RadiantUI_Constants.Hero.CYAN_HEX}>✓ {UiStrings.Get("card.visited")}</color>  " : "") + RichText.Neutralize(card.Meta) + "</nobr>";
        _leave.Slot.ActiveSelf = card.CanLeave;

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
            _tags[i].Label.Content.Value = show ? card.Tags[i].Text : ""; // read back by the press handler, so not wrapped; the mappers strip markup
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
        _button.ColorDrivers[0].SetColors(enabled ? Theme.ButtonSelected : Theme.ButtonQuiet);

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
        if (!Discover.Core.Model.ThumbnailPolicy.TryAccept(raw, out var uri))
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

}
