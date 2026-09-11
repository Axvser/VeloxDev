using Demo.Models;
using Microsoft.AspNetCore.Components;
using VeloxDev.TransitionSystem;

namespace Demo.Components.Pages;

public partial class Home : ComponentBase, IDisposable
{
    // ---------------------------------------------------------------
    // ViewModel instances — animations operate directly on their properties
    // ---------------------------------------------------------------
    // Single source for the three initial colors: the reset below has to restore them, so the literals live here
    // rather than being repeated.
    private const string Box0Color = "#00bcd4";
    private const string Box1Color = "#66bb6a";
    private const string Box2Color = "#ab47bc";

    private BoxModel Box0 { get; } = new() { Color = Box0Color };
    private BoxModel Box1 { get; } = new() { Color = Box1Color };
    private BoxModel Box2 { get; } = new() { Color = Box2Color };

    // ---------------------------------------------------------------
    // Animation definitions (mirroring the three animations of the WPF/Avalonia Demo)
    // ---------------------------------------------------------------

    // Animation0: simple animation — translate + color + opacity, auto reverse loop
    private static readonly Transition<BoxModel> Animation0 =
        Transition<BoxModel>.Create()
            .Property(b => b.X, 500)
            .Property(b => b.Color, "#ff7043")
            .Property(b => b.Opacity, 0.2)
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(2),
                IsAutoReverse = true,
                LoopTime = 2,
                Ease = Eases.Sine.InOut,
            });

    // Animation1: delayed animation — rotate + scale after a 2 second wait
    private static readonly Transition<BoxModel> Animation1 =
        Transition<BoxModel>.Create()
            .Await(TimeSpan.FromSeconds(2))
            .Property(b => b.Rotate, 360)
            .Property(b => b.Scale, 1.5)
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(3),
                IsAutoReverse = true,
                LoopTime = 4,
                FPS = 60,
                Ease = Eases.Circ.InOut,
            });

    // Animation2: combined animation — move right first, then recolor + shrink after a 3s wait
    private static readonly Transition<BoxModel> Animation2 =
        Transition<BoxModel>.Create()
            .Property(b => b.X, 400)
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(2),
                Ease = Eases.Expo.Out,
            })
            .AwaitThen(TimeSpan.FromSeconds(3))
            .Property(b => b.Color, "#ffee58")
            .Property(b => b.Scale, 0.6)
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(1.5),
                IsAutoReverse = true,
                LoopTime = 2,
                Ease = Eases.Bounce.Out,
            });

    protected override void OnInitialized()
    {
        // Blazor animation targets POCO ViewModels with no dispatcher affinity, so a background
        // thread cannot infer the circuit context. It must be captured here (OnInitialized, on the
        // circuit thread). This is an inherent limitation of the Blazor model.
        UIThreadInspector.CaptureUIThread();

        // Subscribe to property changes to drive Blazor re-rendering
        Box0.PropertyChanged += (_, _) => InvokeAsync(StateHasChanged);
        Box1.PropertyChanged += (_, _) => InvokeAsync(StateHasChanged);
        Box2.PropertyChanged += (_, _) => InvokeAsync(StateHasChanged);
    }

    private void LoadMainThread()
    {
        // Start directly on the main (circuit) thread; mutual exclusion (CanMutualTask: true by default)
        Animation0.Execute(Box0);
        Animation1.Execute(Box1);
        Animation2.Execute(Box2);
    }

    private void LoadAnimations()
    {
        // Can also be started from a non-UI thread; the framework switches automatically
        _ = Task.Run(() =>
        {
            Animation0.Execute(Box0);
            Animation1.Execute(Box1);
            Animation2.Execute(Box2);
        });
    }

    private void LoadMainThreadNonMutual()
    {
        // Main thread + CanMutualTask: false — run concurrently, neither cancels the other
        Animation0.Execute(Box0, CanMutualTask: false);
        Animation1.Execute(Box1, CanMutualTask: false);
        Animation2.Execute(Box2, CanMutualTask: false);
    }

    private void LoadAnimationsNonMutual()
    {
        // CanMutualTask: false — the three animations run concurrently without interference and
        // are not cancelled by one another
        _ = Task.Run(() =>
        {
            Animation0.Execute(Box0, CanMutualTask: false);
            Animation1.Execute(Box1, CanMutualTask: false);
            Animation2.Execute(Box2, CanMutualTask: false);
        });
    }

    private void LoadRepeatedMutual()
    {
        // Each click starts a mutually-exclusive animation on Box0: the new animation cancels the
        // previous one (tests scheduler gating and cancellation).
        _ = Task.Run(() => Animation0.Execute(Box0));
    }

    private void ResetBox0()
    {
        // Reset all: stop everything, then replay the initial state as a transition — the same kind of thing as any
        // other animation.
        Transition.Exit(Box0, IncludeMutual: true, IncludeNoMutual: true);
        Transition.Exit(Box1, IncludeMutual: true, IncludeNoMutual: true);
        Transition.Exit(Box2, IncludeMutual: true, IncludeNoMutual: true);

        CreateReset(Box0Color).Effect(TransitionEffects.Empty).Execute(Box0);
        CreateReset(Box1Color).Effect(TransitionEffects.Empty).Execute(Box1);
        CreateReset(Box2Color).Effect(TransitionEffects.Empty).Execute(Box2);
    }

    // The BoxModel defaults, expressed as explicit paths. Color is animatable here as well — the Razor adapter
    // registers a sampler for string — and the three boxes start from different colors, so each box needs its own
    // reset rather than one shared instance.
    private static Transition<BoxModel> CreateReset(string color)
    {
        return Transition<BoxModel>.Create()
            .Property(b => b.X, 0)
            .Property(b => b.Y, 0)
            .Property(b => b.Width, 120)
            .Property(b => b.Height, 80)
            .Property(b => b.Opacity, 1)
            .Property(b => b.Rotate, 0)
            .Property(b => b.Scale, 1)
            .Property(b => b.Color, color);
    }

    private void ExitAnimations()
    {
        // IncludeMutual   indicates whether to end animations configured with CanMutualTask: true
        // IncludeNoMutual indicates whether to end animations configured with CanMutualTask: false
        Transition.Exit(Box0, IncludeMutual: true, IncludeNoMutual: true);
        Transition.Exit(Box1, IncludeMutual: true, IncludeNoMutual: true);
        Transition.Exit(Box2, IncludeMutual: true, IncludeNoMutual: true);
    }

    public void Dispose()
    {
        ExitAnimations();
    }
}
