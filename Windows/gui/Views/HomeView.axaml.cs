using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace JackBridge.GUI.Views;

public partial class HomeView : UserControl
{
    private bool _activityAutoScroll = true;
    private bool _connectionsAutoScroll = true;

    public HomeView()
    {
        InitializeComponent();

        ActivityLogText.PropertyChanged += (s, e) =>
        {
            if (e.Property.Name == "Text" && _activityAutoScroll)
                ActivityScroller.ScrollToEnd();
        };

        ConnectionsLogText.PropertyChanged += (s, e) =>
        {
            if (e.Property.Name == "Text" && _connectionsAutoScroll)
                ConnectionsScroller.ScrollToEnd();
        };
    }

    private void OnLogScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;

        bool atBottom = sv.Offset.Y + sv.Viewport.Height >= sv.Extent.Height - 20;

        if (sv == ActivityScroller)
            _activityAutoScroll = atBottom;
        else if (sv == ConnectionsScroller)
            _connectionsAutoScroll = atBottom;
    }
}
