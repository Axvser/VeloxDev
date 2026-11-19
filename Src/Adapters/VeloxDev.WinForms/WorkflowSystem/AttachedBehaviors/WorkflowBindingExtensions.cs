using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Forms;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// The binding layer a WinForms view needs and the framework does not give it.
/// </summary>
/// <remarks>
/// <para>
/// WinForms has no binding engine worth the name for this job — <c>DataBindings</c> wants a
/// <c>BindingSource</c> per property and says nothing about "the model changed on a background thread".
/// These extensions are the small piece that was missing: apply now, re-apply when the model says it changed,
/// marshal onto the control's thread, and let go of the model when the control is disposed.
/// </para>
/// <para>
/// They are for <b>your own</b> views. The views this package ships bind through their attachments
/// (<c>WorkflowNodeAttachment</c> and friends) and do not need these.
/// </para>
/// <para>
/// The thread marshalling is <see cref="ModelChangeRelay"/> — the same one the shipped views use, so this
/// repository keeps exactly one copy of that easy-to-get-wrong code.
/// </para>
/// </remarks>
public static class WorkflowBindingExtensions
{
    /// <summary>
    /// Applies <paramref name="apply"/> to <paramref name="model"/> now, and again whenever it reports a change.
    /// </summary>
    /// <typeparam name="TModel">The model type.</typeparam>
    /// <param name="control">The view; it owns the subscription and is used to reach the UI thread.</param>
    /// <param name="model">The model to follow, or <see langword="null"/> to bind nothing.</param>
    /// <param name="apply">What to do with the model; the view is passed back so the caller needs no field.</param>
    /// <returns>A subscription that ends when it is disposed or the control is.</returns>
    public static IDisposable Bind<TModel>(this Control control, TModel? model, Action<Control, TModel> apply)
        where TModel : class, INotifyPropertyChanged
    {
        if (control is null) throw new ArgumentNullException(nameof(control));
        if (apply is null) throw new ArgumentNullException(nameof(apply));

        return new ModelSubscription<TModel>(control, model, apply);
    }

    /// <summary>
    /// Projects <paramref name="model"/> through <paramref name="selector"/> and applies the result now, and again
    /// whenever the projection changes.
    /// </summary>
    /// <typeparam name="TModel">The model type.</typeparam>
    /// <typeparam name="TValue">The projected value.</typeparam>
    /// <param name="control">The view; it owns the subscription and is used to reach the UI thread.</param>
    /// <param name="model">The model to follow, or <see langword="null"/> to bind nothing.</param>
    /// <param name="selector">Reads the value the view cares about out of the model.</param>
    /// <param name="apply">What to do with the value.</param>
    /// <returns>A subscription that ends when it is disposed or the control is.</returns>
    /// <remarks>
    /// The projection is what decides whether to re-apply: <paramref name="model"/> raising any property at all
    /// only costs a <paramref name="selector"/> call. Use this to follow one property without a per-property
    /// string, as in <c>view.Bind(node, n =&gt; n.Anchor, (v, a) =&gt; v.Location = …)</c>.
    /// </remarks>
    public static IDisposable Bind<TModel, TValue>(
        this Control control,
        TModel? model,
        Func<TModel, TValue> selector,
        Action<Control, TValue> apply)
        where TModel : class, INotifyPropertyChanged
    {
        if (control is null) throw new ArgumentNullException(nameof(control));
        if (selector is null) throw new ArgumentNullException(nameof(selector));
        if (apply is null) throw new ArgumentNullException(nameof(apply));

        return new ProjectionSubscription<TModel, TValue>(control, model, selector, apply);
    }

    /// <summary>
    /// Reports every change to <paramref name="source"/> on the control's thread.
    /// </summary>
    /// <param name="control">The view; it owns the subscription and is used to reach the UI thread.</param>
    /// <param name="source">The collection to follow (for a workflow tree, its helper's <c>VisibleItems</c>).</param>
    /// <param name="onChange">What to do about it.</param>
    /// <returns>A subscription that ends when it is disposed or the control is.</returns>
    public static IDisposable BindCollection(
        this Control control,
        INotifyCollectionChanged? source,
        Action<Control, NotifyCollectionChangedEventArgs> onChange)
    {
        if (control is null) throw new ArgumentNullException(nameof(control));
        if (onChange is null) throw new ArgumentNullException(nameof(onChange));

        return new CollectionSubscription(control, source, onChange);
    }

    // 三条订阅共用的骨架：控件销毁即解订。控件比模型活得短时，这是唯一可靠的那一端。
    private abstract class SubscriptionBase : IDisposable
    {
        protected SubscriptionBase(Control control)
        {
            Control = control;
            control.Disposed += OnDisposed;
        }

        protected Control Control { get; }

        protected ModelChangeRelay? Relay { get; set; }

        public void Dispose()
        {
            Control.Disposed -= OnDisposed;
            Release();
        }

        protected virtual void Release() => Relay?.Clear();

        private void OnDisposed(object? sender, EventArgs e) => Dispose();
    }

    private sealed class ModelSubscription<TModel> : SubscriptionBase
        where TModel : class, INotifyPropertyChanged
    {
        private readonly Action<Control, TModel> _apply;
        private TModel? _model;

        internal ModelSubscription(Control control, TModel? model, Action<Control, TModel> apply)
            : base(control)
        {
            _apply = apply;
            Relay = new ModelChangeRelay(control, _ => Reapply());
            Set(model);
        }

        private void Set(TModel? model)
        {
            _model = model;
            Relay!.Set(model);
            Reapply();
        }

        private void Reapply()
        {
            if (_model is { } model)
            {
                _apply(Control, model);
            }
        }

        protected override void Release()
        {
            _model = null;
            base.Release();
        }
    }

    private sealed class ProjectionSubscription<TModel, TValue> : SubscriptionBase
        where TModel : class, INotifyPropertyChanged
    {
        private readonly Func<TModel, TValue> _selector;
        private readonly Action<Control, TValue> _apply;
        private TModel? _model;
        private bool _hasValue;
        private TValue? _lastValue;

        internal ProjectionSubscription(
            Control control,
            TModel? model,
            Func<TModel, TValue> selector,
            Action<Control, TValue> apply)
            : base(control)
        {
            _selector = selector;
            _apply = apply;
            Relay = new ModelChangeRelay(control, _ => Reapply());
            _model = model;
            Relay.Set(model);
            Reapply();
        }

        private void Reapply()
        {
            if (_model is not { } model)
            {
                return;
            }

            var value = _selector(model);
            if (_hasValue && EqualityComparer<TValue>.Default.Equals(value, _lastValue!))
            {
                return;
            }

            _hasValue = true;
            _lastValue = value;
            _apply(Control, value);
        }

        protected override void Release()
        {
            _model = null;
            _hasValue = false;
            _lastValue = default;
            base.Release();
        }
    }

    private sealed class CollectionSubscription : SubscriptionBase
    {
        private readonly Action<Control, NotifyCollectionChangedEventArgs> _onChange;
        private INotifyCollectionChanged? _source;

        internal CollectionSubscription(
            Control control,
            INotifyCollectionChanged? source,
            Action<Control, NotifyCollectionChangedEventArgs> onChange)
            : base(control)
        {
            _onChange = onChange;
            _source = source;
            if (source is not null)
            {
                source.CollectionChanged += OnCollectionChanged;
            }
        }

        private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (Control.InvokeRequired)
            {
                Control.BeginInvoke(new NotifyCollectionChangedEventHandler(OnCollectionChanged), sender, e);
                return;
            }

            _onChange(Control, e);
        }

        protected override void Release()
        {
            if (_source is not null)
            {
                _source.CollectionChanged -= OnCollectionChanged;
                _source = null;
            }

            base.Release();
        }
    }
}
