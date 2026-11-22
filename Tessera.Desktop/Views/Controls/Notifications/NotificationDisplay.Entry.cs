using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Tessera.Desktop;

public sealed partial class NotificationDisplay
{
    private sealed class Entry
    {
        private static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(240);
        private readonly NotificationDisplay _display;
        private readonly TranslateTransform _offset = new();
        private AnimationClock? _countdown;
        private DispatcherOperation? _entrance;
        private DoubleAnimation? _exit;
        private TimeSpan _duration;

        public NotificationCard Card { get; } = new() { Margin = new Thickness(0, 4, 0, 4) };
        public bool IsExiting => _exit is not null;

        public Entry(NotificationDisplay display)
        {
            _display = display;
            Card.RenderTransform = _offset;
            Card.DismissButton.Click += (_, _) => Dismiss();
            Card.MouseEnter += (_, _) => UpdateCountdownPause();
            Card.MouseLeave += (_, _) => UpdateCountdownPause();
            Card.IsKeyboardFocusWithinChanged += (_, _) => UpdateCountdownPause();
            Card.Loaded += (_, _) =>
            {
                if (Card.Visibility == Visibility.Visible) QueueEntrance();
            };
        }

        public void Show(string message, NotificationKind kind)
        {
            bool appearing = Card.Visibility != Visibility.Visible;
            _duration = TimeSpan.FromSeconds(kind is NotificationKind.Warning or NotificationKind.Error ? 8 : 4);
            Card.Message.Text = message;
            Card.UpdateContent(kind);
            if (appearing)
            {
                Card.Opacity = 0;
                Card.Visibility = Visibility.Visible;
                if (Card.IsLoaded) QueueEntrance();
            }
            else if (Card.IsLoaded && _entrance is null)
            {
                if (IsExiting)
                {
                    var (property, _) = SlideEdge();
                    double offset = (double)_offset.GetValue(property);
                    StopSlide();
                    if (SystemParameters.ClientAreaAnimation) SlideIn(property, offset);
                }
                StartCountdown();
            }
            Card.Announce();
        }

        public void Stop()
        {
            _entrance?.Abort();
            _entrance = null;
            Card.Visibility = Visibility.Collapsed;
            StopCountdown();
            StopSlide();
        }

        private void Dismiss()
        {
            if (Card.Visibility != Visibility.Visible || IsExiting) return;
            if (!Card.IsLoaded || Card.Opacity == 0 || !SystemParameters.ClientAreaAnimation)
            {
                _display.Remove(this);
                return;
            }

            StopCountdown(keepProgress: true);
            var (property, outside) = SlideEdge();
            var exit = CreateSlide((double)_offset.GetValue(property), outside, EasingMode.EaseIn);
            _exit = exit;
            exit.Completed += (_, _) =>
            {
                if (ReferenceEquals(_exit, exit)) _display.Remove(this);
            };
            _offset.BeginAnimation(property, exit);
        }

        private void QueueEntrance()
        {
            if (_entrance is not null) return;
            _entrance = Card.Dispatcher.InvokeAsync(StartEntrance, DispatcherPriority.Loaded);
        }

        private void StartEntrance()
        {
            _entrance = null;
            if (!Card.IsLoaded || Card.Visibility != Visibility.Visible) return;
            if (SystemParameters.ClientAreaAnimation)
            {
                var (property, outside) = SlideEdge();
                SlideIn(property, outside);
            }
            Card.Opacity = 1;
            StartCountdown();
        }

        private (DependencyProperty Property, double Outside) SlideEdge()
        {
            Point origin = Card.TranslatePoint(new Point(), _display) - new Vector(_offset.X, _offset.Y);
            return _display._column.HorizontalAlignment switch
            {
                HorizontalAlignment.Left => (TranslateTransform.XProperty, -origin.X - Card.ActualWidth),
                HorizontalAlignment.Right => (TranslateTransform.XProperty, _display.ActualWidth - origin.X),
                _ when _display._column.VerticalAlignment == VerticalAlignment.Top => (TranslateTransform.YProperty, -origin.Y - Card.ActualHeight),
                _ => (TranslateTransform.YProperty, _display.ActualHeight - origin.Y)
            };
        }

        private static DoubleAnimation CreateSlide(double from, double to, EasingMode easing) =>
            new(from, to, SlideDuration) { EasingFunction = new CubicEase { EasingMode = easing } };

        private void SlideIn(DependencyProperty property, double from)
        {
            var entrance = CreateSlide(from, 0, EasingMode.EaseOut);
            entrance.FillBehavior = FillBehavior.Stop;
            _offset.BeginAnimation(property, entrance);
        }

        public void StopSlide()
        {
            _exit = null;
            _offset.BeginAnimation(TranslateTransform.XProperty, null);
            _offset.BeginAnimation(TranslateTransform.YProperty, null);
        }

        private void StartCountdown()
        {
            StopCountdown();
            var timeline = new DoubleAnimation(1, 0, _duration);
            var clock = (AnimationClock)timeline.CreateClock(true);
            clock.Completed += (_, _) =>
            {
                if (ReferenceEquals(_countdown, clock)) Dismiss();
            };
            _countdown = clock;
            Card.CountdownScale.ApplyAnimationClock(ScaleTransform.ScaleXProperty, clock);
            UpdateCountdownPause();
        }

        private void StopCountdown(bool keepProgress = false)
        {
            var fill = Card.CountdownScale;
            double remaining = keepProgress ? fill.ScaleX : 1;
            var clock = _countdown;
            if (clock is not null)
            {
                _countdown = null;
                clock.Controller?.Remove();
                fill.ApplyAnimationClock(ScaleTransform.ScaleXProperty, null);
            }
            fill.ScaleX = remaining;
        }

        private void UpdateCountdownPause()
        {
            var controller = _countdown?.Controller;
            if (Card.IsMouseOver || Card.IsKeyboardFocusWithin) controller?.Pause();
            else controller?.Resume();
        }
    }
}
