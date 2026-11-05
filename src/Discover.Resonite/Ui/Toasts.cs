using FrooxEngine;

namespace Discover.Resonite.Ui;

/// <summary>The dash toast the mod uses for notices and errors. Safe from any thread (NotificationPanel marshals itself; V19).</summary>
internal static class Toasts
{
    public static void Show(string message) =>
        NotificationPanel.ShowNotification(null!, message, null!, RadiantUI_Constants.Neutrals.DARK, NotificationType.ToastOnly);
}
