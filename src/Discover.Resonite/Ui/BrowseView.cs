using Discover.Core.Lenses;
using Discover.Core.Presentation;

namespace Discover.Resonite.Ui;

public enum FilterOp
{
    MinUsersUp,
    MinUsersDown,
    MinVisitsUp,
    MinVisitsDown,
    ToggleHasRoom,
    ToggleMobile,
    ToggleVr,
    ToggleOtherVersions,
    HostAny,
    HostPerson,
    HostHeadless,
    Reset,
}

/// <summary>The four buttons shown above a named list.</summary>
public enum ListTool
{
    Rename,
    MoveUp,
    MoveDown,
    Delete,
}

/// <summary>Everything the screen shows, computed by the controller and bound by <see cref="DiscoverUi"/>.</summary>
internal sealed record BrowseView(
    LensKind Lens,
    IReadOnlyList<RailSection> Rail,
    string Title,
    string Subtitle,
    string Count,
    string SortLabel,
    int FilterCount,
    IReadOnlyList<TagChip> Chips,
    IReadOnlyList<CardModel> Cards,
    string? EmptyMessage,
    string? EmptyHint,
    bool ShowClearFilters,
    string? Banner,
    string Status,
    bool HasPrevious,
    bool HasNext,
    IReadOnlyList<SortItem> SortMenu,
    bool Descending,
    BrowseFilters Filters,
    bool ShowOtherVersions,
    bool SessionLens,
    bool ListTools = false,
    bool ShowNewList = true);

/// <summary>One list in the organize panel, and whether the world is in it.</summary>
internal sealed record OrganizeList(string Id, string Name, bool Member);

/// <summary>The organize panel's content: one world's lists and personal tags.</summary>
internal sealed record OrganizeModel(string Key, string Title, IReadOnlyList<OrganizeList> Lists, IReadOnlyList<string> Tags);

/// <summary>The settings the panel shows.</summary>
internal sealed record SettingsModel(bool Animations, bool LiveOnly, string StartLens, Discover.Core.Settings.DiscoverSettings Settings);

internal enum SettingKey { Animations, LiveOnly, StartLens }

/// <summary>What the screen's controls do. Called on the update thread, from button and text-field events.</summary>
internal interface IBrowseActions
{
    void SelectLens(LensKind lens);
    void SelectList(string listId);
    void SearchTextChanged(string text);
    void ChooseSort(SortKey key);
    void SetDescending(bool descending);
    void Filter(FilterOp op);
    void CycleChip(string tag);
    void ClearChips();
    void Refresh();
    void Retry();
    void PreviousPage();
    void NextPage();
    void CardPressed(string key);
    void CardTagPressed(string tag, bool personal);

    void ToggleFavorite(string key);
    void OpenOrganize(string key);
    void NewListPrompt();
    void ListToolPressed(ListTool tool);
    void OrganizeToggleList(string listId);
    void OrganizeAddTag(string tag);
    void OrganizeRemoveTag(string tag);
    void OrganizeNewList();
    void OpenSettings();
    void OpenDetails(string key);
    void ToggleSetting(SettingKey key);
    void ToggleLens(LensKind lens);
    void ToggleSection(RailSectionId section);
    void ExportLibrary();
    void ImportLibrary();

    /// <summary>Known tags matching what is typed (the search bar, or the organize panel's tag field).</summary>
    IReadOnlyList<string> SuggestTags(string typed, bool forSearch);
}
