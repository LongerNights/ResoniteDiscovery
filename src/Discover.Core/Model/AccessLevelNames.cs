namespace Discover.Core.Model;

public static class AccessLevelNames
{
    /// <summary>
    /// Maps a SkyFrost.Base.SessionAccessLevel name to <see cref="AccessLevel"/> (verification V17). The engine enum has
    /// aliases (Friends = Contacts, FriendsOfFriends = ContactsPlus), and <c>ToString()</c> may return either spelling.
    /// Anything unknown becomes <see cref="AccessLevel.Private"/>, which is never listed.
    /// </summary>
    public static AccessLevel FromName(string? name) => name switch
    {
        "Private" => AccessLevel.Private,
        "LAN" => AccessLevel.LAN,
        "Contacts" or "Friends" => AccessLevel.Contacts,
        "ContactsPlus" or "FriendsOfFriends" => AccessLevel.ContactsPlus,
        "RegisteredUsers" => AccessLevel.RegisteredUsers,
        "Anyone" => AccessLevel.Anyone,
        _ => AccessLevel.Private,
    };

    /// <summary>Maps a SkyFrost.Base.OutputDevice name; null or unknown is <see cref="OutputDevice.Unknown"/>.</summary>
    public static OutputDevice DeviceFromName(string? name) => name switch
    {
        "Screen" => OutputDevice.Screen,
        "VR" => OutputDevice.VR,
        "Camera" => OutputDevice.Camera,
        _ => OutputDevice.Unknown,
    };
}
