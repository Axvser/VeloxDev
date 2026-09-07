using System.Windows;
using System.Windows.Input;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors
{
    /// <summary>
    /// Attached link(edge) interaction for workflow link views, cohesive at the attached-property layer — the host
    /// (tree) needs no interaction code-behind. A single master switch (<see cref="IsEnabledProperty"/>) arms the
    /// behavior on a link view; while armed, each pointer state is delivered through WPF's **native** mouse events
    /// (no commands, no custom events) over two fully optional channels:
    /// <list type="bullet">
    /// <item><b>Per-link attached handlers</b> — <see cref="PointerEnteredProperty"/>/<see cref="PointerLeavedProperty"/>
    /// (WPF <see cref="MouseEventHandler"/>) and <see cref="PointerPressedProperty"/>/<see cref="PointerReleasedProperty"/>
    /// (WPF <see cref="MouseButtonEventHandler"/>), set on the link view and invoked with that link's native event args.</item>
    /// <item><b>Static global events</b> — <see cref="PointerEntered"/>/<see cref="PointerLeaved"/>/<see cref="PointerPressed"/>/<see cref="PointerReleased"/>
    /// fire for every armed link view; convenient for an observer (e.g. an UHD overlay) that does not own each link view.
    /// Sender is the link view element (its DataContext is the <see cref="IWorkflowLinkViewModel"/>, carrying the Core
    /// runtime id).</item>
    /// </list>
    /// Handlers are invoked with the native <see cref="MouseEventArgs"/>/<see cref="MouseButtonEventArgs"/> the state was
    /// detected from. Everything — whether the switch is on, whether per-link handlers are bound, whether the global
    /// events are subscribed — is optional and decided at the consumer's scope.
    ///
    /// <b>Keyboard</b> uses the same two-layer shape while a link holds keyboard focus (native <see cref="KeyEventArgs"/>),
    /// for both <b>KeyDown</b> and <b>KeyUp</b>: per-link <see cref="KeyDownProperty"/>/<see cref="KeyUpProperty"/> handlers
    /// and/or the static global <see cref="KeyDown"/>/<see cref="KeyUp"/> events. Subscribers filter the key (e.g.
    /// <see cref="Key.Delete"/> to delete the link under the mouse/focused link) themselves. The link view automatically
    /// takes keyboard focus on pointer-enter and releases it on pointer-leave (see the NOTE below), so keys reach the
    /// hovered link without any click/press. After delivery each key event is marked handled so it stops bubbling —
    /// otherwise GUI/framework features drift in (e.g. ScrollViewer Home/End/PageUp/PageDown viewport scroll).
    ///
    /// Semantics:
    /// <list type="bullet">
    /// <item><b>Entered</b> fires when the pointer moves over the link's polyline; <b>Leaved</b> when it leaves it.</item>
    /// <item><b>Pressed</b> fires on a left-button press over the polyline; <b>Released</b> on the matching up (the view is
    /// mouse-captured, so Release still fires if the pointer was dragged off the line). A press directly on the line also
    /// implies Entered first.</item>
    /// <item>The link is targeted by geometric hit-testing (distance to the same 4-segment polyline the link view renders),
    /// not by element bounds — trimmed link views span the whole canvas, so element hit-testing must stay off elsewhere.
    /// An armed link claims the press (captured + handled) so Pressed always reaches whichever channel is subscribed.</item>
    /// <item>Virtual drag-preview links (endpoints not mounted to a node) are never interactive.</item>
    /// </list>
    ///
    /// NOTE on focus / scroll: on pointer-enter the link view automatically takes keyboard focus; on pointer-leave it is
    /// released (<c>Keyboard.ClearFocus</c>) and the view's original Focusable is restored. WPF otherwise scrolls a focused
    /// element into view, so while armed this behavior swallows <see cref="FrameworkElement.RequestBringIntoViewEvent"/> on
    /// the link view — the hover auto-focus (or any BringIntoView) therefore never makes an enclosing ScrollViewer jump to
    /// the (whole-canvas) link.
    ///
    /// <example>
    /// Convenient global subscription (overlay/UHD style — one subscribe, receives every armed link):
    /// <code>
    /// WorkflowLinkBehaviors.PointerEntered += OnLinkPointerEntered;
    /// WorkflowLinkBehaviors.PointerPressed += OnLinkPointerPressed;
    /// // handlers are native WPF MouseEventHandler / MouseButtonEventHandler:
    /// void OnLinkPointerEntered(object? sender, MouseEventArgs e)
    /// {
    ///     var link = (sender as FrameworkElement)?.DataContext as IWorkflowLinkViewModel;
    ///     var runtimeId = (link as IWorkflowIdentifiable)?.RuntimeId;
    ///     // e is the native WPF mouse event argument of the state that fired.
    /// }
    /// </code>
    /// Per-link variant (where a link view is realized): set <c>IsEnabled</c> + the attached handler properties
    /// (<c>SetPointerEntered(linkView, handler)</c>, …) instead of the global events.
    ///
    /// Keyboard (e.g. Delete the link while it is hovered/focused):
    /// <code>
    /// WorkflowLinkBehaviors.KeyDown += (s, e) =&gt;
    /// {
    ///     if (e.Key != Key.Delete) return;
    ///     var link = (s as FrameworkElement)?.DataContext as IWorkflowLinkViewModel;
    ///     link?.DeleteCommand?.Execute(null);          // or the per-link attached WorkflowLinkBehaviors.SetKeyDown(...)
    /// };
    /// </code>
    /// </example>
    /// </summary>
    public sealed class WorkflowLinkBehaviors : DependencyObject
    {
        private const double HitRadius = 6.0;
        private const double Phi = 0.6180339887;

        public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(WorkflowLinkBehaviors),
            new PropertyMetadata(false, OnIsEnabledChanged));

        public static readonly DependencyProperty PointerEnteredProperty = DependencyProperty.RegisterAttached(
            "PointerEntered",
            typeof(MouseEventHandler),
            typeof(WorkflowLinkBehaviors),
            new PropertyMetadata(null));

        public static readonly DependencyProperty PointerLeavedProperty = DependencyProperty.RegisterAttached(
            "PointerLeaved",
            typeof(MouseEventHandler),
            typeof(WorkflowLinkBehaviors),
            new PropertyMetadata(null));

        public static readonly DependencyProperty PointerPressedProperty = DependencyProperty.RegisterAttached(
            "PointerPressed",
            typeof(MouseButtonEventHandler),
            typeof(WorkflowLinkBehaviors),
            new PropertyMetadata(null));

        public static readonly DependencyProperty PointerReleasedProperty = DependencyProperty.RegisterAttached(
            "PointerReleased",
            typeof(MouseButtonEventHandler),
            typeof(WorkflowLinkBehaviors),
            new PropertyMetadata(null));

        public static readonly DependencyProperty KeyDownProperty = DependencyProperty.RegisterAttached(
            "KeyDown",
            typeof(KeyEventHandler),
            typeof(WorkflowLinkBehaviors),
            new PropertyMetadata(null));

        public static readonly DependencyProperty KeyUpProperty = DependencyProperty.RegisterAttached(
            "KeyUp",
            typeof(KeyEventHandler),
            typeof(WorkflowLinkBehaviors),
            new PropertyMetadata(null));

        private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
            "State",
            typeof(LinkState),
            typeof(WorkflowLinkBehaviors),
            new PropertyMetadata(null));

        public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

        public static MouseEventHandler? GetPointerEntered(DependencyObject element) => (MouseEventHandler?)element.GetValue(PointerEnteredProperty);
        public static void SetPointerEntered(DependencyObject element, MouseEventHandler? value) => element.SetValue(PointerEnteredProperty, value);
        public static MouseEventHandler? GetPointerLeaved(DependencyObject element) => (MouseEventHandler?)element.GetValue(PointerLeavedProperty);
        public static void SetPointerLeaved(DependencyObject element, MouseEventHandler? value) => element.SetValue(PointerLeavedProperty, value);
        public static MouseButtonEventHandler? GetPointerPressed(DependencyObject element) => (MouseButtonEventHandler?)element.GetValue(PointerPressedProperty);
        public static void SetPointerPressed(DependencyObject element, MouseButtonEventHandler? value) => element.SetValue(PointerPressedProperty, value);
        public static MouseButtonEventHandler? GetPointerReleased(DependencyObject element) => (MouseButtonEventHandler?)element.GetValue(PointerReleasedProperty);
        public static void SetPointerReleased(DependencyObject element, MouseButtonEventHandler? value) => element.SetValue(PointerReleasedProperty, value);

        public static KeyEventHandler? GetKeyDown(DependencyObject element) => (KeyEventHandler?)element.GetValue(KeyDownProperty);
        public static void SetKeyDown(DependencyObject element, KeyEventHandler? value) => element.SetValue(KeyDownProperty, value);
        public static KeyEventHandler? GetKeyUp(DependencyObject element) => (KeyEventHandler?)element.GetValue(KeyUpProperty);
        public static void SetKeyUp(DependencyObject element, KeyEventHandler? value) => element.SetValue(KeyUpProperty, value);

        // ---- Static global events (convenient observer channel, e.g. an UHD overlay) -------------------------
        // Fired for every armed link view alongside the per-link attached handlers. Handlers receive the native WPF
        // mouse/key event args of the state; sender is the link view element (its DataContext is the IWorkflowLinkViewModel,
        // so subscribers can read the Core RuntimeId from it). Subscribe / unsubscribe freely — everything optional.

        public static event MouseEventHandler? PointerEntered;
        public static event MouseEventHandler? PointerLeaved;
        public static event MouseButtonEventHandler? PointerPressed;
        public static event MouseButtonEventHandler? PointerReleased;

        /// <summary>Global key-down feed for every armed link view that holds keyboard focus (auto-acquired on pointer
        /// enter). Handlers filter the key (e.g. <see cref="Key.Delete"/>) themselves; sender is the focused link view.</summary>
        public static event KeyEventHandler? KeyDown;

        /// <summary>Global key-up feed — mirror of <see cref="KeyDown"/> for the matching release.</summary>
        public static event KeyEventHandler? KeyUp;

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement element)
            {
                return;
            }

            if (Equals(e.NewValue, true))
            {
                Attach(element);
            }
            else
            {
                Detach(element);
            }
        }

        private static void Attach(FrameworkElement element)
        {
            Detach(element);

            var state = new LinkState { OriginalHitTestVisible = element.IsHitTestVisible, OriginalFocusable = element.Focusable };
            element.SetValue(StateProperty, state);

            element.DataContextChanged += OnDataContextChanged;
            element.AddHandler(UIElement.MouseMoveEvent, new MouseEventHandler(OnMouseMove), true);
            element.MouseLeave += OnMouseLeave;
            element.AddHandler(UIElement.MouseLeftButtonDownEvent, new MouseButtonEventHandler(OnMouseLeftButtonDown), true);
            element.AddHandler(UIElement.MouseLeftButtonUpEvent, new MouseButtonEventHandler(OnMouseLeftButtonUp), true);
            element.LostMouseCapture += OnLostMouseCapture;
            element.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnKeyDown), true);
            element.AddHandler(UIElement.KeyUpEvent, new KeyEventHandler(OnKeyUp), true);
            element.AddHandler(FrameworkElement.RequestBringIntoViewEvent, new RequestBringIntoViewEventHandler(OnRequestBringIntoView), true);

            UpdateHitTest(element, state);
        }

        private static void Detach(FrameworkElement element)
        {
            var state = (LinkState?)element.GetValue(StateProperty);
            if (state is null)
            {
                return;
            }

            element.DataContextChanged -= OnDataContextChanged;
            element.RemoveHandler(UIElement.MouseMoveEvent, new MouseEventHandler(OnMouseMove));
            element.MouseLeave -= OnMouseLeave;
            element.RemoveHandler(UIElement.MouseLeftButtonDownEvent, new MouseButtonEventHandler(OnMouseLeftButtonDown));
            element.RemoveHandler(UIElement.MouseLeftButtonUpEvent, new MouseButtonEventHandler(OnMouseLeftButtonUp));
            element.LostMouseCapture -= OnLostMouseCapture;
            element.RemoveHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnKeyDown));
            element.RemoveHandler(UIElement.KeyUpEvent, new KeyEventHandler(OnKeyUp));
            element.RemoveHandler(FrameworkElement.RequestBringIntoViewEvent, new RequestBringIntoViewEventHandler(OnRequestBringIntoView));

            if (Equals(Mouse.Captured, element))
            {
                element.ReleaseMouseCapture();
            }

            element.IsHitTestVisible = state.OriginalHitTestVisible;
            element.Focusable = state.OriginalFocusable;
            if (element.IsKeyboardFocused)
            {
                Keyboard.ClearFocus();
            }

            element.ClearValue(StateProperty);
        }

        /// <summary>Whether the element is a real (non-virtual) link whose strokes are drawn — the only case hit-testing may be on.</summary>
        private static bool IsRealLink(FrameworkElement element, out IWorkflowLinkViewModel? link)
        {
            link = element.DataContext as IWorkflowLinkViewModel;
            return link is not null
                && !(link.Sender?.Parent is null && link.Receiver?.Parent is null);
        }

        private static void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is not FrameworkElement element)
            {
                return;
            }

            if (element.GetValue(StateProperty) is LinkState state)
            {
                UpdateHitTest(element, state);
            }
        }

        private static void UpdateHitTest(FrameworkElement element, LinkState state)
        {
            // Armed (attached only while IsEnabled) real link → hit-testing on, regardless of whether handlers are
            // bound — subscribers may use the per-link attached handlers and/or the static global events.
            bool interactive = IsRealLink(element, out _);
            element.IsHitTestVisible = interactive;

            // The pooled link view may have been recycled (DataContext dropped or swapped) while the pointer was
            // hovering — drop the interaction state cleanly (no native args available to synthesize a Leaved here).
            if (!interactive && state.Hovering)
            {
                state.Hovering = false;
                state.HoveredLink = null;
                state.Pressed = false;
                if (Equals(Mouse.Captured, element))
                {
                    element.ReleaseMouseCapture();
                }
            }

            // A recycled/hidden link must not keep keyboard focus or stay focusable (keyboard would otherwise go nowhere).
            if (!interactive)
            {
                element.Focusable = state.OriginalFocusable;
                if (element.IsKeyboardFocused)
                {
                    Keyboard.ClearFocus();
                }
            }
        }

        private static void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement element || element.GetValue(StateProperty) is not LinkState state)
            {
                return;
            }

            if (!IsRealLink(element, out var link) || link is null || !link.IsRenderReady())
            {
                return;
            }

            var position = e.GetPosition(element);
            bool over = IsOverPolyline(link, position);
            if (over && !state.Hovering)
            {
                state.Hovering = true;
                state.HoveredLink = link;
                FireEntered(element, e);
                AcquireHoverFocus(element, state);
            }
            else if (!over && state.Hovering)
            {
                state.Hovering = false;
                state.HoveredLink = null;
                FireLeaved(element, e);
                ReleaseHoverFocus(element, state);
            }
        }

        private static void OnMouseLeave(object sender, MouseEventArgs e)
        {
            // The pointer stopped being over this element's stroke — regardless of whether an intermediate
            // MouseMove arrived, close a pending hover.
            if (sender is not FrameworkElement element || element.GetValue(StateProperty) is not LinkState state)
            {
                return;
            }

            if (!state.Hovering)
            {
                return;
            }

            state.Hovering = false;
            state.HoveredLink = null;
            FireLeaved(element, e);
            ReleaseHoverFocus(element, state);
        }

        private static void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.GetValue(StateProperty) is not LinkState state)
            {
                return;
            }

            if (e.ChangedButton != MouseButton.Left || e.ButtonState != MouseButtonState.Pressed)
            {
                return;
            }

            if (!IsRealLink(element, out var link) || link is null || !link.IsRenderReady())
            {
                return;
            }

            var position = e.GetPosition(element);
            if (!IsOverPolyline(link, position))
            {
                return; // press off the line — let the surface / node-drag handlers deal with it
            }

            if (!state.Hovering)
            {
                // A direct press on the line implies the pointer first entered it (MouseButtonEventArgs is a MouseEventArgs).
                state.Hovering = true;
                state.HoveredLink = link;
                FireEntered(element, e);
                AcquireHoverFocus(element, state);
            }

            // An armed link claims the press (capture + handled) so Pressed always fires — to an attached handler
            // and/or the static global PointerPressed event.
            state.Pressed = true;
            element.CaptureMouse();
            FirePressed(element, e);
            e.Handled = true;
        }

        private static void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.GetValue(StateProperty) is not LinkState state)
            {
                return;
            }

            if (e.ChangedButton != MouseButton.Left || !state.Pressed)
            {
                return;
            }

            state.Pressed = false;
            if (Equals(Mouse.Captured, element))
            {
                element.ReleaseMouseCapture();
            }

            // Capture guarantees we received the up even if the pointer was dragged off the line; the hover state
            // (Entered/Leaved) is driven separately by geometry and is left untouched here.
            if (IsRealLink(element, out _))
            {
                FireReleased(element, e);
            }

            e.Handled = true;
        }

        private static void OnLostMouseCapture(object sender, MouseEventArgs e)
        {
            // Capture was lost without an up (e.g. another element took it, or the view was recycled). Drop the
            // pressed latch; Entered/Leaved bookkeeping is handled by geometry paths.
            if (sender is FrameworkElement element && element.GetValue(StateProperty) is LinkState state)
            {
                state.Pressed = false;
            }
        }

        private static void OnKeyDown(object sender, KeyEventArgs e)
        {
            // Native KeyDown only reaches this link view while it holds keyboard focus (auto-acquired on pointer
            // enter). Subscribers (per-link KeyDown + global KeyDown) see the event synchronously first; then the
            // event is consumed here so it stops bubbling — otherwise GUI/framework features drift in once focus sits
            // on a link inside a ScrollViewer (End/Home/PageUp/PageDown scrolling the viewport, Tab focus move, …).
            if (sender is not FrameworkElement element || element.GetValue(StateProperty) is not LinkState)
            {
                return;
            }

            if (!IsRealLink(element, out _))
            {
                return;
            }

            FireKeyDown(element, e);
            e.Handled = true;
        }

        private static void OnKeyUp(object sender, KeyEventArgs e)
        {
            // Mirror of OnKeyDown for the matching key release.
            if (sender is not FrameworkElement element || element.GetValue(StateProperty) is not LinkState)
            {
                return;
            }

            if (!IsRealLink(element, out _))
            {
                return;
            }

            FireKeyUp(element, e);
            e.Handled = true;
        }

        private static void OnRequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
        {
            // A link view spans the whole canvas; if anything focuses it, WPF would ask an enclosing ScrollViewer to
            // bring it into view and jump the viewport. Swallow the request so GetFocus never scrolls to the link.
            e.Handled = true;
        }

        /// <summary>Pointer entered the link → give it keyboard focus (so a key like Delete acts on the hovered link).
        /// RequestBringIntoView is swallowed while armed, so focusing never makes a ScrollViewer scroll to the link.</summary>
        private static void AcquireHoverFocus(FrameworkElement element, LinkState state)
        {
            element.Focusable = true;
            element.Focus();
        }

        /// <summary>Pointer left the link → release keyboard focus and restore the view's original focusability.</summary>
        private static void ReleaseHoverFocus(FrameworkElement element, LinkState state)
        {
            if (element.IsKeyboardFocused)
            {
                Keyboard.ClearFocus();
            }

            element.Focusable = state.OriginalFocusable;
        }

        // Dispatch a state through both channels: the per-link attached handler (if bound) and the static global event.
        private static void FireEntered(FrameworkElement element, MouseEventArgs e)
        {
            GetPointerEntered(element)?.Invoke(element, e);
            PointerEntered?.Invoke(element, e);
        }

        private static void FireLeaved(FrameworkElement element, MouseEventArgs e)
        {
            GetPointerLeaved(element)?.Invoke(element, e);
            PointerLeaved?.Invoke(element, e);
        }

        private static void FirePressed(FrameworkElement element, MouseButtonEventArgs e)
        {
            GetPointerPressed(element)?.Invoke(element, e);
            PointerPressed?.Invoke(element, e);
        }

        private static void FireReleased(FrameworkElement element, MouseButtonEventArgs e)
        {
            GetPointerReleased(element)?.Invoke(element, e);
            PointerReleased?.Invoke(element, e);
        }

        private static void FireKeyDown(FrameworkElement element, KeyEventArgs e)
        {
            GetKeyDown(element)?.Invoke(element, e);
            KeyDown?.Invoke(element, e);
        }

        private static void FireKeyUp(FrameworkElement element, KeyEventArgs e)
        {
            GetKeyUp(element)?.Invoke(element, e);
            KeyUp?.Invoke(element, e);
        }

        private static bool IsOverPolyline(IWorkflowLinkViewModel link, Point point)
        {
            var anchor = GetPoints(link);
            if (anchor is null)
            {
                return false;
            }

            var points = anchor;
            for (int i = 0; i < points.Length - 1; i++)
            {
                if (DistanceToSegment(point, points[i], points[i + 1]) <= HitRadius)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Rebuilds the same 4-segment polyline the link view draws, straight from the endpoint slot anchors (never the view's DPs).</summary>
        private static Point[]? GetPoints(IWorkflowLinkViewModel link)
        {
            var senderAnchor = link.Sender?.Anchor;
            var receiverAnchor = link.Receiver?.Anchor;
            if (senderAnchor is null || receiverAnchor is null)
            {
                return null;
            }

            var start = new Point(senderAnchor.Horizontal, senderAnchor.Vertical);
            var end = new Point(receiverAnchor.Horizontal, receiverAnchor.Vertical);
            if (double.IsNaN(start.X) || double.IsNaN(start.Y) || double.IsNaN(end.X) || double.IsNaN(end.Y))
            {
                return null;
            }

            double dx = end.X - start.X;
            double stub = dx / 2.0 * (1.0 - Phi);
            return new[]
            {
                start,
                new Point(start.X + stub, start.Y),
                new Point(end.X - stub, end.Y),
                end
            };
        }

        private static double DistanceToSegment(Point p, Point a, Point b)
        {
            double abx = b.X - a.X;
            double aby = b.Y - a.Y;
            double lengthSquared = abx * abx + aby * aby;
            if (lengthSquared < 0.0001)
            {
                double vx = p.X - a.X;
                double vy = p.Y - a.Y;
                return System.Math.Sqrt(vx * vx + vy * vy);
            }

            double t = ((p.X - a.X) * abx + (p.Y - a.Y) * aby) / lengthSquared;
            if (t < 0.0) t = 0.0;
            else if (t > 1.0) t = 1.0;
            double cx = a.X + t * abx;
            double cy = a.Y + t * aby;
            double ddx = p.X - cx;
            double ddy = p.Y - cy;
            return System.Math.Sqrt(ddx * ddx + ddy * ddy);
        }

        /// <summary>Per-element interaction bookkeeping, held in a private attached DP (repo pattern).</summary>
        private sealed class LinkState
        {
            public bool OriginalHitTestVisible;
            public bool OriginalFocusable;
            public bool Hovering;
            public bool Pressed;
            public IWorkflowLinkViewModel? HoveredLink;
        }
    }
}
