using System.Windows;
using System.Windows.Threading;
using Tessera.WpfStyles.Themes;

namespace DomainColorTest;

/// <summary>Keeps static theme notifications within the lifetime and dispatcher of a preview.</summary>
internal sealed class PreviewThemeSubscription
{
    private readonly FrameworkElement _owner;
    private readonly Action _refresh;
    private Window? _window;
    private EventHandler? _themeHandler;
    private bool _subscribed;

    public PreviewThemeSubscription(FrameworkElement owner, Action refresh)
    {
        _owner = owner;
        _refresh = refresh;
        owner.Loaded += Loaded;
        owner.Unloaded += Unloaded;
    }

    private void Loaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed)
        {
            _subscribed = true;
            var weak = new WeakReference<PreviewThemeSubscription>(this);
            EventHandler? handler = null;
            handler = (_, _) =>
            {
                if (weak.TryGetTarget(out var subscription)) subscription.ThemeChanged();
                else ThemeManager.ThemeChanged -= handler;
            };
            _themeHandler = handler;
            ThemeManager.ThemeChanged += handler;
            _owner.Dispatcher.ShutdownStarted += LifetimeEnded;
            _window = Window.GetWindow(_owner);
            if (_window is not null) _window.Closed += LifetimeEnded;
        }
        RefreshIfLoaded();
    }

    private void Unloaded(object sender, RoutedEventArgs e) => Detach();
    private void LifetimeEnded(object? sender, EventArgs e) => Detach();

    private void Detach()
    {
        if (!_subscribed) return;
        _subscribed = false;
        ThemeManager.ThemeChanged -= _themeHandler;
        _themeHandler = null;
        _owner.Dispatcher.ShutdownStarted -= LifetimeEnded;
        if (_window is not null) _window.Closed -= LifetimeEnded;
        _window = null;
    }

    private void ThemeChanged()
    {
        var dispatcher = _owner.Dispatcher;
        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
        if (dispatcher.CheckAccess())
        {
            RefreshIfLoaded();
            return;
        }
        try
        {
            dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(RefreshIfLoaded));
        }
        catch (InvalidOperationException) when (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            // The dispatcher may shut down between the check and enqueueing the notification.
        }
    }

    private void RefreshIfLoaded()
    {
        if (_subscribed && !_owner.Dispatcher.HasShutdownStarted && !_owner.Dispatcher.HasShutdownFinished && _owner.IsLoaded)
            _refresh();
    }
}
