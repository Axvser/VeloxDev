using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Demo.Views
{
    /// <summary>
    /// CanvasItemsControl's container synchronizes the Canvas.Left/Top on the template/child
    /// element onto the container itself, so that when the ItemsPanel is a Canvas, the
    /// Canvas.Left/Top bound on the child element actually takes effect.
    /// </summary>
    public sealed partial class CanvasItemsControl : ItemsControl
    {
        protected override DependencyObject GetContainerForItemOverride() => new CanvasItemContainer();

        protected override bool IsItemItsOwnContainerOverride(object item) => item is CanvasItemContainer;

        /// <summary>
        /// Container: finds the generated visual child under the ContentPresenter (the root of the
        /// DataTemplate), listens for its Canvas.Left/Top changes, and syncs the values onto the
        /// container itself (the container is positioned on the ItemsPanel's Canvas).
        /// </summary>
        private sealed partial class CanvasItemContainer : ContentPresenter
        {
            private FrameworkElement? _childElement;
            private long _leftToken = 0;
            private long _topToken = 0;
            private bool _isHooked = false;

            public CanvasItemContainer()
            {
                Loaded += OnLoaded;
                Unloaded += OnUnloaded;
                // Also listen for Content changes (when DataContext or a ConditionalSlot is replaced in ItemsControl)
                RegisterPropertyChangedCallback(ContentProperty, (_, __) => OnContentChanged());
            }

            private void OnLoaded(object? s, RoutedEventArgs e)
            {
                TryHookGeneratedChild();
            }

            private void OnUnloaded(object? s, RoutedEventArgs e)
            {
                UnhookChild();
            }

            private void OnContentChanged()
            {
                UnhookChild();
                TryHookGeneratedChild();
            }

            // Sometimes the template has not generated its visual subtree yet; retry via LayoutUpdated until found
            private void TryHookGeneratedChild()
            {
                if (_isHooked) return;

                var found = FindGeneratedChild();
                if (found != null)
                {
                    HookChild(found);
                    return;
                }

                // If not generated yet, wait for a one-shot retry on LayoutUpdated
                LayoutUpdated += OnLayoutUpdatedRetry;
            }

            private void OnLayoutUpdatedRetry(object? sender, object? e)
            {
                LayoutUpdated -= OnLayoutUpdatedRetry;
                if (_isHooked) return;
                var found = FindGeneratedChild();
                if (found != null) HookChild(found);
            }

            private FrameworkElement? FindGeneratedChild()
            {
                // Usually the 0th visual child of the ContentPresenter is the template root
                if (VisualTreeHelper.GetChildrenCount(this) > 0)
                {
                    var first = VisualTreeHelper.GetChild(this, 0) as FrameworkElement;
                    return first;
                }

                // Another case: Content is a direct UIElement (e.g. you put a UIElement directly into Items);
                // in that case the element may be this.Content, so try returning it
                if (Content is FrameworkElement fe) return fe;

                return null;
            }

            private void HookChild(FrameworkElement child)
            {
                if (_isHooked && _childElement == child) return;

                UnhookChild();

                _childElement = child;

                // Register Canvas.Left/Top attached-property change callbacks (WinUI API returns a token)
                _leftToken = _childElement.RegisterPropertyChangedCallback(Canvas.LeftProperty, OnAttachedPositionChanged);
                _topToken = _childElement.RegisterPropertyChangedCallback(Canvas.TopProperty, OnAttachedPositionChanged);

                // Sync the position once (if the binding has taken effect, this reads the value)
                // Also re-sync when the child is Loaded, in case the binding completes after Loaded
                _childElement.Loaded += Child_Loaded;

                UpdatePositionFromChild();

                _isHooked = true;
            }

            private void UnhookChild()
            {
                if (!_isHooked || _childElement == null) return;

                try
                {
                    if (_leftToken != 0)
                    {
                        _childElement.UnregisterPropertyChangedCallback(Canvas.LeftProperty, _leftToken);
                        _leftToken = 0;
                    }
                    if (_topToken != 0)
                    {
                        _childElement.UnregisterPropertyChangedCallback(Canvas.TopProperty, _topToken);
                        _topToken = 0;
                    }
                    _childElement.Loaded -= Child_Loaded;
                }
                catch
                {
                    // Ignore possible exceptions (e.g. already unloaded)
                }

                _childElement = null;
                _isHooked = false;
            }

            private void Child_Loaded(object? s, RoutedEventArgs e)
            {
                // The binding may push values only after Loaded; ensure another sync
                UpdatePositionFromChild();
            }

            private void OnAttachedPositionChanged(DependencyObject dp, DependencyProperty prop)
            {
                // When the child's Canvas.Left/Top changes, sync immediately to the container
                UpdatePositionFromChild();
            }

            private void UpdatePositionFromChild()
            {
                if (_childElement == null) return;

                double x = Canvas.GetLeft(_childElement);
                double y = Canvas.GetTop(_childElement);

                // If the child does not set it (NaN), do not force 0 (though 0 is usually desired); here follow the original intent: NaN -> 0
                if (double.IsNaN(x)) x = 0;
                if (double.IsNaN(y)) y = 0;

                Canvas.SetLeft(this, x);
                Canvas.SetTop(this, y);
            }
        }
    }
}
