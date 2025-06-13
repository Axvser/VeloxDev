using System.Windows;
using System.Windows.Input;

namespace VeloxDev.WPF.HotKey
{
    internal sealed class LocalHotKeyInjector : IDisposable
    {
        internal readonly WeakReference<FrameworkElement> _targetWeakRef;
        internal readonly HashSet<Key> _pressedKeys = [];
        internal readonly HashSet<Key> _targetKeys;

        internal event KeyEventHandler? KeyEventInvoked;

        internal LocalHotKeyInjector(FrameworkElement target, HashSet<Key> keys, KeyEventHandler handler)
        {
            _targetWeakRef = new WeakReference<FrameworkElement>(target);
            _targetKeys = keys;
            KeyEventInvoked += handler;

            if (TryGetTarget(out var element) && element is not null)
            {
                element.Focusable = true;
                element.AddHandler(FrameworkElement.PreviewKeyDownEvent, (KeyEventHandler)OnPreviewKeyDown, true);
                element.AddHandler(FrameworkElement.PreviewKeyUpEvent, (KeyEventHandler)OnPreviewKeyUp, true);
                element.AddHandler(FrameworkElement.LostKeyboardFocusEvent, (RoutedEventHandler)OnLostFocus, true);
                element.AddHandler(FrameworkElement.UnloadedEvent, (RoutedEventHandler)OnUnloaded, true);
            }
        }

        private bool TryGetTarget(out FrameworkElement? target) => _targetWeakRef.TryGetTarget(out target);

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (TryGetTarget(out _))
            {
                _pressedKeys.Add(e.Key == Key.System ? e.SystemKey : e.Key);
                CheckInvoke(e);
            }
        }
        private void OnPreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (TryGetTarget(out _))
            {
                _pressedKeys.Remove(e.Key == Key.System ? e.SystemKey : e.Key);
                CheckInvoke(e);
            }
        }
        private void OnLostFocus(object sender, RoutedEventArgs e) => _pressedKeys.Clear();
        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Dispose();
        }
        private void CheckInvoke(KeyEventArgs e)
        {
            if (_pressedKeys.SetEquals(_targetKeys))
            {
                if (TryGetTarget(out var target))
                {
                    KeyEventInvoked?.Invoke(target, e);
                    _pressedKeys.Clear();
                }
            }
        }

        public void Dispose()
        {
            KeyEventInvoked = null;
            if (TryGetTarget(out var target) && target is not null)
            {
                target.RemoveHandler(FrameworkElement.PreviewKeyDownEvent, (KeyEventHandler)OnPreviewKeyDown);
                target.RemoveHandler(FrameworkElement.PreviewKeyUpEvent, (KeyEventHandler)OnPreviewKeyUp);
                target.RemoveHandler(FrameworkElement.LostKeyboardFocusEvent, (RoutedEventHandler)OnLostFocus);
            }
            GC.SuppressFinalize(this);
        }
    }
}