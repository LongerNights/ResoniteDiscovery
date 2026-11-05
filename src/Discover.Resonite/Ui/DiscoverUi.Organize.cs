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
                label.Content.Value = "<nobr>+ " + RichText.Neutralize(CardText.Ellipsize(found[i], 12)) + "</nobr>";
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
            Fix(label, TypeScale.Label);
            label.HorizontalAlign.Value = TextHorizontalAlignment.Left;
            if (label.Slot.GetComponent<RectTransform>() is { } labelRect)
            {
                labelRect.OffsetMin.Value = new float2(16f, 0f);
                labelRect.OffsetMax.Value = new float2(-8f, 0f);
            }

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
                label.Content.Value = "<nobr>tag:" + RichText.Neutralize(CardText.Ellipsize(found[i], 24)) + "</nobr>";
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
        _organizeTitle.Content.Value = "<nobr>" + RichText.Neutralize(CardText.Ellipsize(model.Title, 40)) + "</nobr>";

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
                label.Content.Value = "<nobr>" + RichText.Neutralize(CardText.Ellipsize(list.Name, 28)) + "</nobr>";
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
                label.Content.Value = "<nobr>× " + RichText.Neutralize(CardText.Ellipsize(model.Tags[i], 12)) + "</nobr>";
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
            AnimateOpen(_organizePanel, ModalRise, _organizeScrim);
        }
    }
}
