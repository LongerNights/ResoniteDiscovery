namespace Discover.Core.Model;

/// <summary>
/// The link the detail panel copies for a published world: the record URL <c>resrec:///OwnerId/RecordId</c> that the
/// engine's own "copy URL" puts on the clipboard for inventory items (FrooxEngine.Store.Record.GetUrl). Never a session URL.
/// </summary>
public static class RecordUrls
{
    public static string? For(DiscoveryEntry entry)
    {
        if (entry.Record is { OwnerId.Length: > 0, RecordId.Length: > 0 } record)
        {
            return $"resrec:///{record.OwnerId}/{record.RecordId}";
        }

        // A live world that has not been hydrated has no record yet, but its key is the record id (OwnerId:RecordId).
        if (DiscoveryKeys.IsWorldKey(entry.Key) && entry.Key.IndexOf(':') is var split and > 0 && split < entry.Key.Length - 1)
        {
            return $"resrec:///{entry.Key[..split]}/{entry.Key[(split + 1)..]}";
        }

        return null;
    }
}
