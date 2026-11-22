using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;

namespace Tessera.Desktop;

internal enum NotificationKind { Information, Success, Warning, Error }

public partial class NotificationCard : UserControl
{
    internal TextBlock Message => statusLabel;
    internal Button DismissButton => dismiss;
    internal ScaleTransform CountdownScale => countdownScale;

    private const string IconOutline = "M 9,1 A 8,8 0 1 1 9,17 A 8,8 0 1 1 9,1 Z ";
    private static readonly Geometry InformationIcon = Geometry.Parse(IconOutline + "M 6.5,6.5 C 6.5,3.8 11.5,3.8 11.5,6.5 C 11.5,8.3 9,8.3 9,10 M 9,12.7 L 9,12.8");
    private static readonly Geometry SuccessIcon = Geometry.Parse(IconOutline + "M 5,9 L 8,12 L 13,6");
    private static readonly Geometry AttentionIcon = Geometry.Parse(IconOutline + "M 9,5 L 9,9.8 M 9,12.7 L 9,12.8");
    public NotificationCard()
    {
        InitializeComponent();
        dismiss.ToolTip = Localization.Text("Close");
        AutomationProperties.SetName(dismiss, (string)dismiss.ToolTip);
    }

    private void CardSurfaceSizeChanged(object sender, SizeChangedEventArgs e) => cardOutline.Rect = new Rect(e.NewSize);

    internal void UpdateContent(NotificationKind kind)
    {
        var (title, icon, brush) = kind switch
        {
            NotificationKind.Success => ("Done", SuccessIcon, "NotificationSuccess"),
            NotificationKind.Warning => ("Warning", InformationIcon, "NotificationInformation"),
            NotificationKind.Error => ("Error", AttentionIcon, "NotificationAttention"),
            _ => ("Notification", InformationIcon, "NotificationInformation")
        };
        heading.Text = Localization.Text(title);
        kindIcon.Data = icon;
        kindIcon.Stroke = (Brush)Resources[brush];
        AutomationProperties.SetName(this, heading.Text + ". " + Message.Text);
    }

    internal void Announce()
    {
        if (AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
            UIElementAutomationPeer.CreatePeerForElement(this)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
