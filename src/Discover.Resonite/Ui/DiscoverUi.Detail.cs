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
    private string? _detailShownSession;
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
        _detailHomeMin = slot.GetComponent<RectTransform>().AnchorMin.Value;
        _detailHomeMax = slot.GetComponent<RectTransform>().AnchorMax.Value;
        (_detailSheet, _detailSheetRect) = SweepSheet(ui);
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
        var (catcher, _) = Theme.Catcher(ui, "Hover");
        var panner = catcher.Slot.AttachComponent<Perspective360Panner>();
        // Gentler than the engine's default: the full-speed pan makes the picture whirl as the pointer enters.
        panner.HorizontalPanningAcceleration.Value *= 0.35f;
        panner.HorizontalPanningSpeed.Value *= 0.6f;
        block.PerspectiveAngleOffset.DriveFrom(panner.AngleOffset);
        block.PerspectiveFieldOfView.DriveFrom(panner.FOV);
        ui.NestOut();
    }

    private void SetDetailThumbnail(string? raw)
    {
        if (!Discover.Core.Model.ThumbnailPolicy.TryAccept(raw, out var uri))
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
        if (_detailShownSession != session.SessionId)
        {
            _detailShownSession = session.SessionId;
            UiMotion.FadeIn(_detailUptime.Color, Theme.Text, 0.22f);
            UiMotion.FadeIn(_detailVisibility.Color, Theme.Text, 0.22f);
            UiMotion.FadeIn(_detailUsersLine.Color, Theme.Text, 0.22f);
            UiMotion.FadeIn(_detailPeople.Color, Theme.Text, 0.30f);
        }

        _detailUptime.Content.Value = "<nobr>" + RichText.Neutralize(session.Uptime) + "</nobr>";
        _detailVisibility.Content.Value = "<nobr>" + RichText.Neutralize(session.Visibility) + "</nobr>";
        _detailUsersLine.Content.Value = "<nobr>" + RichText.Neutralize(session.UsersLine) + "</nobr>";
        var names = session.Users.Take(24).Select(u => u.IsContact
            ? $"<color={ContactColorHex}>{RichText.Neutralize(CardText.Ellipsize(u.Name, 20))}</color>"
            : RichText.Neutralize(CardText.Ellipsize(u.Name, 20)));
        _detailPeople.Content.Value = string.Join(", ", names);

        // A joinable session is joined by the main button; a full one says so.
        if (_detailModel is { Action: CardAction.Join })
        {
            _detailActionLabel.Content.Value = UiStrings.Get(session.CanJoin ? "card.join" : "card.full");
            _detailAction.Enabled = session.CanJoin;
            _detailAction.ColorDrivers[0].SetColors(session.CanJoin ? Theme.ButtonSelected : Theme.ButtonQuiet);
        }
    }

    /// <summary>The card (or row) the next detail panel opens from; set by the card just before it asks for details.</summary>
    internal static Slot? PendingOrigin;

    private Slot _detailSheet = null!;
    private RectTransform _detailSheetRect = null!;
    private int _scanGeneration;

    /// <summary>
    /// Once the panel has grown into place, a dark sheet over it (the panel's own colour, with a cyan leading edge)
    /// retracts from the top down, so the contents are scanned in instead of appearing at once.
    /// </summary>
    private void CoverDetail(float2 min, float2 max)
    {
        _scanGeneration++;
        _detailSheet.ActiveSelf = true;
        _detailSheetRect.AnchorMin.Value = min;
        _detailSheetRect.AnchorMax.Value = max;
    }

    private void ScanDetail(float2 min, float2 max)
    {
        try
        {
            var generation = ++_scanGeneration;
            _detailSheet.ActiveSelf = true;
            _detailSheetRect.AnchorMin.Value = min;
            _detailSheetRect.AnchorMax.Value = max;
            _detailSheetRect.AnchorMax.TweenFromTo(max, new float2(max.x, min.y), UiMotion.Seconds(0.42f), CurvePreset.QuarticOut, onDoneLocal: () =>
            {
                if (generation == _scanGeneration)
                {
                    _detailSheet.ActiveSelf = false;
                }
            });
        }
        catch (Exception ex)
        {
            _detailSheet.ActiveSelf = false;
            UniLog.Warning($"[Discover] Detail scan failed: {ex.GetType().Name}");
        }
    }

    private float2 _detailHomeMin;
    private float2 _detailHomeMax;
    private float _detailHeight = 0.84f;
    private int _expandGeneration;

    /// <summary>Puts the detail panel back as BuildFloating made it (after an expansion that was cut short, or finished).</summary>
    private void RestoreDetailGeometry()
    {
        var rect = _detailPanel.GetComponent<RectTransform>();
        rect.Pivot.Value = new float2(0.5f, 0.5f);
        rect.AnchorMin.Value = _detailHomeMin;
        rect.AnchorMax.Value = _detailHomeMax;
        rect.OffsetMin.Value = float2.Zero;
        rect.OffsetMax.Value = float2.Zero;
        if (_detailPanel.GetComponent<ContentSizeFitter>() is { } fitter)
        {
            fitter.Enabled = true;
        }

        // The children go back to what they were before the expansion hid them (not all on: some are off on purpose for
        // this world, e.g. the session area of a world nobody is in).
        if (_expansionHidden is { } hidden)
        {
            _expansionHidden = null;
            foreach (var (child, wasActive) in hidden)
            {
                if (!child.IsDestroyed)
                {
                    child.ActiveSelf = wasActive;
                }
            }
        }
    }

    private List<(Slot Child, bool WasActive)>? _expansionHidden;

    /// <summary>
    /// The panel grows out of the card (or list row) that was pressed: its own background is stretched over the card's
    /// rectangle, then tweened to the panel's place, with the contents held back until it arrives. Returns false when the
    /// positions cannot be read (the caller then uses the plain rise).
    /// </summary>
    private bool ExpandFromOrigin(Slot origin)
    {
        try
        {
            var rect = _detailPanel.GetComponent<RectTransform>();
            if (_detailPanel.Parent?.GetComponent<RectTransform>() is not { } host
                || origin.GetComponent<RectTransform>() is not { } from
                || _detailPanel.GetComponent<ContentSizeFitter>() is not { } fitter)
            {
                return false;
            }

            var hostRect = host.ComputeGlobalComputeRect();
            var fromRect = from.ComputeGlobalComputeRect();
            if (hostRect.size.x < 1f || hostRect.size.y < 1f || fromRect.size.x < 1f)
            {
                return false;
            }

            var start0 = (fromRect.position - hostRect.position) / hostRect.size;
            var start1 = start0 + fromRect.size / hostRect.size;
            if (start1.x < 0f || start0.x > 1f || start1.y < 0f || start0.y > 1f)
            {
                return false;
            }

            var half = _detailHeight * 0.5f;
            var end0 = new float2(_detailHomeMin.x, _detailHomeMin.y - half);
            var end1 = new float2(_detailHomeMax.x, _detailHomeMax.y + half);

            var generation = ++_expandGeneration;
            fitter.Enabled = false;
            rect.Pivot.Value = new float2(0.5f, 0.5f);
            rect.OffsetMin.Value = float2.Zero;
            rect.OffsetMax.Value = float2.Zero;
            rect.AnchorMin.Value = start0;
            rect.AnchorMax.Value = start1;
            _expansionHidden = _detailPanel.Children.Select(c => (c, c.ActiveSelf)).ToList();
            foreach (var child in _detailPanel.Children)
            {
                child.ActiveSelf = false;
            }

            var seconds = UiMotion.Seconds(0.38f);
            rect.AnchorMin.TweenFromTo(start0, end0, seconds, CurvePreset.QuinticOut);
            rect.AnchorMax.TweenFromTo(start1, end1, seconds, CurvePreset.QuinticOut, onDoneLocal: () =>
            {
                if (generation != _expandGeneration)
                {
                    return;
                }

                RestoreDetailGeometry();

                // The sheet first covers the predicted rectangle at once (no flash of the contents), then, when the layout
                // has settled, it is fitted to the panel's real size and starts to retract.
                CoverDetail(end0, end1);
                _screen.World.RunInUpdates(3, () =>
                {
                    if (generation != _expandGeneration)
                    {
                        return;
                    }

                    var panel = rect.ComputeGlobalComputeRect();
                    var hostNow = host.ComputeGlobalComputeRect();
                    if (hostNow.size.x < 1f || hostNow.size.y < 1f)
                    {
                        ScanDetail(end0, end1);
                        return;
                    }

                    var min = (panel.position - hostNow.position) / hostNow.size;
                    var max = min + panel.size / hostNow.size;
                    if (panel.size.y / hostNow.size.y is > 0.3f and < 1f)
                    {
                        _detailHeight = panel.size.y / hostNow.size.y;
                    }

                    ScanDetail(min, max);
                });
            });
            return true;
        }
        catch (Exception ex)
        {
            UniLog.Warning($"[Discover] Card expansion failed: {ex.GetType().Name}");
            RestoreDetailGeometry();
            return false;
        }
    }

    public void ShowDetails(DetailModel model)
    {
        var origin = PendingOrigin;
        PendingOrigin = null;
        _expandGeneration++;
        _scanGeneration++;
        _detailSheet.ActiveSelf = false;
        RestoreDetailGeometry(); // an expansion that was cut short must not leave the panel as it was mid-way
        CloseMenus();
        _detailKey = model.Key;
        _detailTitle.Content.Value = "<nobr>" + RichText.Neutralize(model.Title) + "</nobr>";
        _detailBy.Content.Value = "<nobr>" + RichText.Neutralize(model.ByLine) + "</nobr>";
        _detailDescription.Content.Value = RichText.Neutralize(model.Description);
        var tagList = model.TagList ?? [];
        for (var t = 0; t < MaxDetailTags; t++)
        {
            var show = t < tagList.Count;
            _detailTagNames[t] = show ? tagList[t] : null;
            _detailTagButtons[t].Button.Slot.ActiveSelf = show;
            _detailTagButtons[t].Label.Content.Value = show ? "<nobr>" + RichText.Neutralize(CardText.Ellipsize(tagList[t], 14)) + "</nobr>" : "";
        }

        _detailTagsHeader.Slot.ActiveSelf = tagList.Count > 0;
        _detailTagGrid.ActiveSelf = tagList.Count > 0;
        SetDetailThumbnail(model.ThumbnailUrl);
        _detailModel = model;
        _detailSessionData = model.SessionRows ?? [];
        _detailSelected = 0;
        _detailShownSession = null;
        _detailSessionArea.Slot.ActiveSelf = _detailSessionData.Count > 0;
        _detailNewSession.Slot.ActiveSelf = model.CanStartNew && _detailSessionData.Count > 0;
        for (var r = 0; r < _detailSessionRows.Count; r++)
        {
            var show = r < _detailSessionData.Count;
            _detailSessionRows[r].Button.Slot.ActiveSelf = show;
            if (show)
            {
                _detailSessionRows[r].Label.Content.Value = "<nobr>" + RichText.Neutralize(_detailSessionData[r].ListText) + "</nobr>";
            }
        }

        _detailSessions.Slot.ActiveSelf = _detailSessionData.Count == 0;
        RenderDetailSession();
        _detailSessions.Content.Value = RichText.Neutralize(model.Sessions);
        _detailDevices.Content.Value = RichText.Neutralize(model.Devices);
        _detailDates.Content.Value = "<nobr>" + RichText.Neutralize(model.Dates) + "</nobr>";
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
        if (origin is not null && Animate && ExpandFromOrigin(origin))
        {
            FadeScrim(_detailScrim);
        }
        else
        {
            AnimateOpen(_detailPanel, ModalRise, _detailScrim);
        }
    }
}
