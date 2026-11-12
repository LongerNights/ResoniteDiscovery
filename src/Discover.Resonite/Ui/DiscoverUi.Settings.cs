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
    // ---- settings panel: the sidebar's sections and lenses on the left, general options and your data on the right

    private Slot _settingsScrim = null!;
    private Slot _settingsPanel = null!;
    private readonly List<Slot> _settingsPages = [];
    private readonly List<Button> _settingsTabs = [];
    private int _settingsTab;
    private bool _settingsListView;

    // Page controls, bound by ShowSettings.
    private (Image Track, RectTransform Knob) _animationsSwitch;
    private (Image Track, RectTransform Knob) _notifyLiveSwitch;
    private (Image Track, RectTransform Knob) _notifyContactsSwitch;
    private Text _startLensValue = null!;
    private Button _startLensRow = null!;
    private Slot _startLensMenu = null!;
    private const int MaxStartLensRows = 16;
    private readonly List<(Button Button, Text Label)> _startLensItems = [];
    private IReadOnlyList<(LensKind Lens, string Label, bool Selected)> _startLensChoices = [];
    private Slider<float> _animationSpeedSlider = null!;
    private Text _animationSpeedLabel = null!;
    private Slider<float> _panSpeedSlider = null!;
    private Text _panSpeedLabel = null!;
    private bool _applyingSettings;
    private Button _viewGrid = null!;
    private Button _viewList = null!;
    private readonly Dictionary<int, Button> _cardsPerRowButtons = [];
    private readonly Dictionary<int, Button> _rowsPerPageButtons = [];
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
            _animationsSwitch = SettingsSwitch(ui, UiStrings.Get("settings.animations"), () => _actions.ToggleSetting(SettingKey.Animations));
            _animationSpeedLabel = SettingsLabel(ui, UiStrings.Get("settings.animationSpeed"));
            _animationSpeedSlider = SettingsSlider(ui, DiscoverSettings.AnimationSpeedMin, DiscoverSettings.AnimationSpeedMax, v => _actions.SetAnimationSpeed(v));
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

            _panSpeedLabel = SettingsLabel(ui, UiStrings.Get("settings.panSpeed"));
            _panSpeedSlider = SettingsSlider(ui, DiscoverSettings.PreviewPanSpeedMin, DiscoverSettings.PreviewPanSpeedMax, v => _actions.SetPreviewPanSpeed(v));

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

            SettingsLabel(ui, UiStrings.Get("settings.rowsPerPage"));
            ui.Style.MinHeight = 46f;
            var rowsRow = ui.HorizontalLayout(6f, 0f, Alignment.MiddleCenter);
            foreach (var count in DiscoverSettings.RowsPerPageChoices)
            {
                var button = ui.Button((LocaleString)count.ToString());
                Fix(button.Slot.GetComponentInChildren<Text>(), TypeScale.Body);
                var captured = count;
                button.LocalPressed += (_, _) => _actions.SetRowsPerPage(captured);
                _rowsPerPageButtons[count] = button;
            }

            ui.NestOut();
        }));

        // Sidebar: three columns of switches (Now, Discover, Yours), then a bar and the Experimental group under them: its
        // heading, the explanation, and the Insights switches side by side in one row.
        _settingsPages.Add(SettingsPage(ui, () =>
        {
            ui.Style.MinHeight = 0f;
            ui.Style.FlexibleHeight = 1f;
            var columns = ui.HorizontalLayout(14f, 0f, Alignment.TopLeft);
            columns.ForceExpandHeight.Value = false;
            ui.Style.FlexibleHeight = -1f;
            foreach (var section in new[] { RailSectionId.Now, RailSectionId.Discover, RailSectionId.Yours })
            {
                ui.Style.FlexibleWidth = 1f;
                var column = ui.VerticalLayout(4f, 0f, Alignment.TopCenter);
                column.ForceExpandHeight.Value = false;
                ui.Style.FlexibleWidth = -1f;
                AddSectionSwitches(ui, section, indentLenses: true);
                ui.NestOut();
            }

            ui.NestOut();

            ui.Style.MinHeight = 3f;
            var bar = ui.Image(Theme.TextSecondary.SetA(0.45f));
            bar.InteractionTarget.Value = false;
            SettingsGroup(ui, "settings.experimental");
            ui.Style.MinHeight = 48f;
            var insightsNote = ui.Text((LocaleString)UiStrings.Get("settings.insightsNote"), bestFit: true, alignment: Alignment.TopLeft);
            Fix(insightsNote, TypeScale.Caption);
            insightsNote.Color.Value = Theme.TextSecondary;

            ui.Style.MinHeight = 42f;
            ui.Style.FlexibleHeight = -1f;
            var insightsRow = ui.HorizontalLayout(6f, 0f, Alignment.TopLeft);
            insightsRow.ForceExpandHeight.Value = false;
            AddSectionSwitches(ui, RailSectionId.Insights, indentLenses: false);
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
            ui.Style.MinHeight = 260f;
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
                _startLensItems[i].Label.Content.Value = "<nobr>" + (selected ? "✓  " : "") + RichText.Neutralize(label) + "</nobr>";
                SetColor(_startLensItems[i].Button, selected ? Theme.ButtonSelected : Theme.Button);
            }
        }

        PositionMenu(_startLensMenu);
        _startLensMenu.ActiveSelf = true;
        AnimateOpen(_startLensMenu, MenuDrop, null, CurvePreset.ReboundOutSmall);
    }

    /// <summary>
    /// A section's own switch and one switch per lens, in the current layout. In a column the lenses are indented under the
    /// section; in a row they sit beside it.
    /// </summary>
    private void AddSectionSwitches(UIBuilder ui, RailSectionId section, bool indentLenses)
    {
        ui.Style.MinHeight = 42f;
        ui.Style.FlexibleWidth = indentLenses ? -1f : 1f;
        var sectionRow = SettingsSwitchRow(ui, "", () => _actions.ToggleSection(section));
        _sectionRows[section] = (sectionRow.Button, sectionRow.Track, sectionRow.Knob);
        foreach (var lens in LensCatalog.LensesOf(section))
        {
            ui.Style.MinHeight = indentLenses ? 36f : 42f;
            ui.Style.FlexibleWidth = indentLenses ? -1f : 1f;
            var kind = lens;
            var lensRow = SettingsSwitchRow(ui, "", () => _actions.ToggleLens(kind));
            if (indentLenses && lensRow.Label.Slot.GetComponent<RectTransform>() is { } lensRect)
            {
                lensRect.OffsetMin.Value = new float2(36f, 0f);
            }

            _lensRows[lens] = (lensRow.Button, lensRow.Track, lensRow.Knob);
        }

        ui.Style.FlexibleWidth = -1f;
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

    private static Text SettingsLabel(UIBuilder ui, string text)
    {
        ui.Style.MinHeight = 30f;
        var label = ui.Text((LocaleString)text, bestFit: true, alignment: Alignment.BottomLeft);
        Fix(label, TypeScale.Body);
        return label;
    }

    /// <summary>A slider under its label. Moving it calls <paramref name="changed"/>; setting it from the model does not.</summary>
    private Slider<float> SettingsSlider(UIBuilder ui, float min, float max, Action<float> changed)
    {
        ui.Style.MinHeight = 40f;
        var slider = ui.Slider<float>(32f, min, min, max, false);
        slider.Value.OnValueChange += field =>
        {
            if (!_applyingSettings)
            {
                changed(field.Value);
                UpdateSliderLabels(field.Value, slider);
            }
        };
        return slider;
    }

    private void UpdateSliderLabels(float value, Slider<float> slider)
    {
        var text = value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "×";
        if (slider == _animationSpeedSlider)
        {
            _animationSpeedLabel.Content.Value = UiStrings.Get("settings.animationSpeed") + ": " + text;
        }
        else if (slider == _panSpeedSlider)
        {
            _panSpeedLabel.Content.Value = UiStrings.Get("settings.panSpeed") + ": " + text;
        }
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
        ApplySwitch(_notifyLiveSwitch.Track, _notifyLiveSwitch.Knob, settings.NotifyFavoriteLive);
        ApplySwitch(_notifyContactsSwitch.Track, _notifyContactsSwitch.Knob, settings.NotifyContactsInFavorite);
        _startLensValue.Content.Value = "<nobr>" + RichText.Neutralize(model.StartLens) + "  ▾</nobr>";
        _startLensChoices = model.StartLensChoices ?? [];

        _applyingSettings = true;
        try
        {
            _animationSpeedSlider.Value.Value = settings.AnimationSpeed;
            _panSpeedSlider.Value.Value = settings.PreviewPanSpeed;
        }
        finally
        {
            _applyingSettings = false;
        }

        UpdateSliderLabels(settings.AnimationSpeed, _animationSpeedSlider);
        UpdateSliderLabels(settings.PreviewPanSpeed, _panSpeedSlider);

        SetColor(_viewGrid, settings.ListView ? Theme.Button : Theme.ButtonSelected);
        SetColor(_viewList, settings.ListView ? Theme.ButtonSelected : Theme.Button);
        var perRow = settings.CardsPerRow > 0 ? settings.CardsPerRow : 4;
        foreach (var (count, button) in _cardsPerRowButtons)
        {
            SetColor(button, count == perRow ? Theme.ButtonSelected : Theme.Button);
        }

        foreach (var (count, button) in _rowsPerPageButtons)
        {
            SetColor(button, count == settings.RowsPerPage ? Theme.ButtonSelected : Theme.Button);
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
                    + RichText.Neutralize(CardText.Ellipsize(item.Name, 34)) + "</nobr>";
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
            row.Button.Slot.GetComponentInChildren<Text>().Content.Value = "<nobr>" + RichText.Neutralize(LensName(lens)) + "</nobr>";
            ApplySwitch(row.Track, row.Knob, switchOn);
            SetColor(row.Button, shown ? Theme.Button : Theme.ButtonQuiet);
        }

        if (!_settingsPanel.ActiveSelf)
        {
            CloseMenus();
            _settingsScrim.ActiveSelf = true;
            _settingsPanel.ActiveSelf = true;
            AnimateOpen(_settingsPanel, ModalRise, _settingsScrim);
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
        AnimateOpen(_promptPanel, ModalRise, _promptScrim);
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
}
