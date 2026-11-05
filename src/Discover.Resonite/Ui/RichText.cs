using Elements.Assets;

namespace Discover.Resonite.Ui;

/// <summary>
/// The one place text from other players (names, descriptions, tags) is made safe for the engine's rich-text renderer.
/// Two different jobs, named for what they do: <see cref="Neutralize"/> for text about to be displayed, <see cref="Strip"/>
/// for text being stored or compared.
/// </summary>
internal static class RichText
{
    /// <summary>Shows markup literally (the engine's <c>&lt;noparse&gt;</c> wrapper; verification V11). For display only.</summary>
    public static string Neutralize(string? value) => StringParsingHelper.SanitizeFormatTags(value) ?? "";

    /// <summary>Removes rich-text tags, leaving plain text (verification V11). For mapping engine data into snapshots.</summary>
    public static string Strip(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        return value.Contains('<') ? new StringRenderTree(value).GetRawString() : value;
    }
}
