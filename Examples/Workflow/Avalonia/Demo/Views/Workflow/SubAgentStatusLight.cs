using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using VeloxDev.AI.SubAgents;
using VeloxDev.TransitionSystem;

namespace Demo;

/// <summary>
/// The lamp on one sub-agent tree node: green while the child is working, grey once it finished, red if it
/// died on an exception.
/// <para>
/// Only the running state breathes. A finished child has nothing left to say about time, so its lamp is
/// still — and a still lamp is also what makes the breathing one readable at a glance, since the eye finds
/// motion before it finds colour.
/// </para>
/// <para>
/// Cancelled is grey rather than red: the library treats a cancellation as a decision the user made, not as
/// an accident (<c>SubAgentScope.Cancel</c>'s remarks, and <c>Finish</c>'s "cancelled" branch).
/// </para>
/// </summary>
public class SubAgentStatusLight : Ellipse
{
    // The cycle is asymmetric on purpose: this is the period of one direction, and IsAutoReverse plays it
    // back, so the full in-and-out breath is twice this. 800ms in / 800ms out reads as breathing rather
    // than as blinking.
    private const double BreathMilliseconds = 800d;

    // A breath that reaches full opacity looks like a blink. Coming up to 1.0 from a dim floor is what
    // makes it a glow.
    private const double Dim = 0.3d;

    private static readonly IBrush RunningBrush = new SolidColorBrush(Color.Parse("#6BFFB8"));
    private static readonly IBrush IdleBrush = new SolidColorBrush(Color.Parse("#6B7280"));
    private static readonly IBrush FailedBrush = new SolidColorBrush(Color.Parse("#FF6B6B"));

    /// <summary>
    /// One infinite in-and-out. Built once and shared by every lamp: a declaration is immutable, and
    /// <c>Execute</c> starts an independent run per target.
    /// <para>
    /// <c>LoopTime = int.MaxValue</c> is the only "forever" this system has — there is no repeat flag
    /// elsewhere. The duration must not be zero: a zero-length pass consumes no time, so a forever loop
    /// over it spins instead of looping, and <c>Transition.Exit</c> can no longer interrupt it.
    /// </para>
    /// </summary>
    private static readonly Transition<SubAgentStatusLight> Breath =
        Transition<SubAgentStatusLight>.Create()
            .Property(l => l.Opacity, 1d)
            .Effect(new TransitionEffect
            {
                Duration = TimeSpan.FromMilliseconds(BreathMilliseconds),
                IsAutoReverse = true,
                LoopTime = int.MaxValue,
                Ease = Eases.Sine.InOut,
            });

    /// <summary>The row this lamp reports on. Bound from the node template.</summary>
    public static readonly StyledProperty<SubAgentStatusViewModel?> RowProperty =
        AvaloniaProperty.Register<SubAgentStatusLight, SubAgentStatusViewModel?>(nameof(Row));

    private SubAgentStatusViewModel? _watched;
    private bool _breathing;

    public SubAgentStatusViewModel? Row
    {
        get => GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == RowProperty)
        {
            Watch(change.GetNewValue<SubAgentStatusViewModel?>());
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Containers are recycled: this lamp may be arriving with a row it has never been shown for, and the
        // state it should report is that row's, not whatever the last occupant left behind.
        Paint();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        // A forever loop outlives the element it animates unless it is stopped: nothing about detaching a
        // control cancels the sampling loop running against it, so a scrolled-away node would keep a
        // dispatcher-bound timer alive and keep writing Opacity to a control nobody can see.
        Stop();
    }

    private void Watch(SubAgentStatusViewModel? row)
    {
        if (ReferenceEquals(_watched, row)) return;

        if (_watched is not null) _watched.PropertyChanged -= OnRowChanged;
        _watched = row;
        if (_watched is not null) _watched.PropertyChanged += OnRowChanged;

        Paint();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        // State is the only thing this lamp reports, and it is the only property that moves without the
        // grant/result fields beside it moving too. Everything else a row raises — call counts, payloads —
        // is noise here, and repainting on it would restart the breath on every tool call.
        if (e.PropertyName is nameof(SubAgentStatusViewModel.State))
            Paint();
    }

    private void Paint()
    {
        var state = _watched?.State ?? SubAgentState.Completed;

        Fill = state switch
        {
            SubAgentState.Running or SubAgentState.Queued => RunningBrush,
            SubAgentState.Failed => FailedBrush,
            _ => IdleBrush,
        };

        if (state is SubAgentState.Running or SubAgentState.Queued) Start();
        else Stop();
    }

    private void Start()
    {
        if (_breathing) return;

        // Unlike PolylineCurveView's flow this lamp can be started and stopped repeatedly over one
        // lifetime, so it exits first: a run left over from a recycling container would otherwise be
        // joined rather than replaced, and the two would fight over Opacity every frame.
        Transition.Exit(this, IncludeMutual: true, IncludeNoMutual: true);

        // The transition reads its start values off the target, so the lamp has to be at the floor before
        // Execute — and because the loop replays that captured frame set at every seam, this is also the
        // state the second breath begins from.
        Opacity = Dim;

        Breath.Execute(this);
        _breathing = true;
    }

    private void Stop()
    {
        if (!_breathing)
        {
            Opacity = 1d;
            return;
        }

        Transition.Exit(this, IncludeMutual: true, IncludeNoMutual: true);
        _breathing = false;

        // Exit freezes where it was, and the breath is forever, so it stops wherever the last frame left it
        // — a still lamp has to be told to be fully lit rather than left at a random point of the cycle.
        Opacity = 1d;
    }
}
