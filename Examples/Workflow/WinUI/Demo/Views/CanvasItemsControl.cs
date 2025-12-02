using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Demo.Views
{
    /// <summary>
    /// CanvasItemsControl 的容器会把模板/子元素上的 Canvas.Left/Top 同步到容器自身，
    /// 以保证在 ItemsPanel 为 Canvas 时，绑定到子元素的 Canvas.Left/Top 能生效。
    /// </summary>
    public sealed partial class CanvasItemsControl : ItemsControl
    {
        protected override DependencyObject GetContainerForItemOverride() => new CanvasItemContainer();

        protected override bool IsItemItsOwnContainerOverride(object item) => item is CanvasItemContainer;

        /// <summary>
        /// Container：查找 ContentPresenter 下生成的视觉子元素（DataTemplate 的根），
        /// 监听该元素的 Canvas.Left/Top，并把值同步到容器自身（Container 在 ItemsPanel 的 Canvas 上定位）。
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
                // 也监听 Content 变化（ItemsControl 里 DataContext 或 Item 被替换时）
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

            // 有时模板还未生成视觉子树，尝试通过 LayoutUpdated 重试直到找到
            private void TryHookGeneratedChild()
            {
                if (_isHooked) return;

                var found = FindGeneratedChild();
                if (found != null)
                {
                    HookChild(found);
                    return;
                }

                // 如果还没生成，等待 LayoutUpdated 一次性重试
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
                // 通常 ContentPresenter 的第 0 个视觉子项就是模板根
                if (VisualTreeHelper.GetChildrenCount(this) > 0)
                {
                    var first = VisualTreeHelper.GetChild(this, 0) as FrameworkElement;
                    return first;
                }

                // 另外一种情形：Content 是直接 UIElement（例如你直接把 UIElement 放到 Items），
                // 在这种情形该元素可能就是 this.Content，尝试返回它
                if (Content is FrameworkElement fe) return fe;

                return null;
            }

            private void HookChild(FrameworkElement child)
            {
                if (_isHooked && _childElement == child) return;

                UnhookChild();

                _childElement = child;

                // 注册 Canvas.Left/Top 附加属性变化回调（WinUI 的 API：返回 token）
                _leftToken = _childElement.RegisterPropertyChangedCallback(Canvas.LeftProperty, OnAttachedPositionChanged);
                _topToken = _childElement.RegisterPropertyChangedCallback(Canvas.TopProperty, OnAttachedPositionChanged);

                // 同步一次位置（如果绑定已生效，这里会把值读出来）
                // 还要在 child Loaded 时再同步一次，防止绑定完成在 Loaded 之后
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
                    // 忽略可能的异常（已卸载等）
                }

                _childElement = null;
                _isHooked = false;
            }

            private void Child_Loaded(object? s, RoutedEventArgs e)
            {
                // 绑定可能在 Loaded 后才推送值，确保再次同步
                UpdatePositionFromChild();
            }

            private void OnAttachedPositionChanged(DependencyObject dp, DependencyProperty prop)
            {
                // 当 child 的 Canvas.Left/Top 发生变化时，马上同步到容器
                UpdatePositionFromChild();
            }

            private void UpdatePositionFromChild()
            {
                if (_childElement == null) return;

                double x = Canvas.GetLeft(_childElement);
                double y = Canvas.GetTop(_childElement);

                // 若子元素上未设置（NaN），不强制为 0（但通常希望 0），这里按照原始期望：NaN -> 0
                if (double.IsNaN(x)) x = 0;
                if (double.IsNaN(y)) y = 0;

                Canvas.SetLeft(this, x);
                Canvas.SetTop(this, y);
            }
        }
    }
}
