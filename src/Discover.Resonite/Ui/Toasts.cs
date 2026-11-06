using System.Reflection;
using Elements.Core;
using FrooxEngine;

namespace Discover.Resonite.Ui;

/// <summary>
/// The dash toast the mod uses for notices and errors. Safe from any thread (NotificationPanel marshals itself; V19).
/// The public <c>NotificationPanel.ShowNotification</c> always titles the toast "N/A" (it calls the private
/// <c>AddNotification</c> with the default <c>mainMessage</c>), so the private overload that takes the title is called
/// directly, as the public one does. If it cannot be found (an engine update), the public call is used instead.
/// </summary>
internal static class Toasts
{
    /// <summary>The toast's title line.</summary>
    private const string Title = "Worlds";

    // VERIFY: NotificationPanel.AddNotification(string userId, string message, Uri thumbnail, colorX color, NotificationType type,
    // string mainMessage, Uri overrideProfile, IAssetProvider<AudioClip> clip) is private; signature from the 2026.10.29 decompile.
    private static readonly MethodInfo? AddNotification = typeof(NotificationPanel).GetMethod(
        "AddNotification",
        BindingFlags.Instance | BindingFlags.NonPublic,
        null,
        [typeof(string), typeof(string), typeof(Uri), typeof(colorX), typeof(NotificationType), typeof(string), typeof(Uri), typeof(IAssetProvider<AudioClip>)],
        null);

    public static void Show(string message)
    {
        var color = RadiantUI_Constants.Neutrals.DARK;
        var panel = NotificationPanel.Current;
        if (panel is null)
        {
            return;
        }

        if (AddNotification is not null)
        {
            try
            {
                panel.RunSynchronously(() =>
                {
                    try
                    {
                        AddNotification.Invoke(panel, [null, message, null, color, NotificationType.ToastOnly, Title, null, null]);
                    }
                    catch (Exception ex)
                    {
                        UniLog.Warning($"[Discover] Toast failed: {ex.GetType().Name}");
                    }
                });
                return;
            }
            catch (Exception ex)
            {
                UniLog.Warning($"[Discover] Toast failed: {ex.GetType().Name}");
            }
        }

        NotificationPanel.ShowNotification(null!, message, null!, color, NotificationType.ToastOnly);
    }
}
