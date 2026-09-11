using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using VeloxDev.TransitionSystem;

namespace Demo;

public partial class MainWindow : Window
{
    private bool _resetInitialized;

    public MainWindow()
    {
        InitializeComponent();

        Rec0.RenderTransform = new TranslateTransform();

        // The overshoot strip drives RenderTransform.X in place, so its transform is created once here.
        Over0.RenderTransform = new TranslateTransform();

        // Reset snapshots are taken only after the element is Loaded, avoiding information loss from
        // an initial state that has not yet been established.
        // Rec0's RenderTransform.X is modified in place, which would pollute the references held by
        // the snapshot, so Rec0 is reset with a new object; Rec1/Rec2 Fills are replaced wholesale
        // (not mutated), so the initial snapshots fully restore them.
        Loaded += (s, e) =>
        {
            if (_resetInitialized) return;
            _resetInitialized = true;

            // Explicitly initialize to a definite state first, so the reset paths below describe a known state.
            Rec0.RenderTransform = new TranslateTransform();
            Rec1.RenderTransform = null;
            Rec2.RenderTransform = null;

            btnReset.Click += (s, e) =>
            {
                Transition.Exit(Rec0, IncludeMutual: true, IncludeNoMutual: true);
                Transition.Exit(Rec1, IncludeMutual: true, IncludeNoMutual: true);
                Transition.Exit(Rec2, IncludeMutual: true, IncludeNoMutual: true);

                // RenderTransform is cleared by direct assignment rather than through the builder: the adapter's
                // Transform overload takes a collection, and an empty one builds an empty TransformGroup, not null.
                Rec1.RenderTransform = null;
                Rec2.RenderTransform = null;

                // Apply the reset synchronously: bypasses the async Execute pipeline
                // (unreliable for Transform reset on some platforms), and Rec0 gets a fresh object
                // each time so its references are not polluted by in-place animation edits.
                ApplyReset(CreateResetRec0(), Rec0);
                ApplyReset(CreateResetRec1(), Rec1);
                ApplyReset(CreateResetRec2(), Rec2);

                ResetOverShoot();
            };

            // Readout for the overshoot strip. Sampled on a timer rather than from the effect's events: the pipeline
            // clones the effect once per segment, so the handlers subscribed here are not the ones that fire.
            var readout = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
            readout.Tick += (s, e) => Readout.Text =
                $"位移 X   目标 {ShiftTarget,6:F1}   当前 {((TranslateTransform)Over0.RenderTransform).X,7:F1}"
                + $"     |     宽度   目标 {WidthTarget,6:F1}   当前 {Over2.Width,7:F1}";
            readout.Start();
        };
    }

    private void LoadMainThread(object sender, RoutedEventArgs e)
    {
        // Start directly on the main (UI) thread; CanMutualTask: true (default) — mutually
        // exclusive, the new animation interrupts the old one
        Animation0.Execute(Rec0);
        Animation1.Execute(Rec1);
        Animation2.Execute(Rec2);
    }

    private void LoadBackground(object sender, RoutedEventArgs e)
    {
        // Started from a non-UI thread; the framework automatically switches back to the UI thread
        // (thread marshaling derived from the target).
        _ = Task.Run(() =>
        {
            Animation0.Execute(Rec0);
            Animation1.Execute(Rec1);
            Animation2.Execute(Rec2);
        });
    }

    private void LoadMainThreadNonMutual(object sender, RoutedEventArgs e)
    {
        // Main thread + CanMutualTask: false — run concurrently, neither cancels the other
        Animation0.Execute(Rec0, CanMutualTask: false);
        Animation1.Execute(Rec1, CanMutualTask: false);
        Animation2.Execute(Rec2, CanMutualTask: false);
    }

    private void LoadBackgroundNonMutual(object sender, RoutedEventArgs e)
    {
        // Non-UI thread + CanMutualTask: false — run concurrently
        _ = Task.Run(() =>
        {
            Animation0.Execute(Rec0, CanMutualTask: false);
            Animation1.Execute(Rec1, CanMutualTask: false);
            Animation2.Execute(Rec2, CanMutualTask: false);
        });
    }

    private void RepeatMutual(object sender, RoutedEventArgs e)
    {
        // Each click starts a mutually-exclusive animation on Rec0, and the new animation cancels
        // the previous one (tests scheduler gating and cancellation).
        _ = Task.Run(() => Animation0.Execute(Rec0));
    }

    private void ExitAll(object sender, RoutedEventArgs e)
    {
        // IncludeMutual   indicates whether to end animations configured with CanMutualTask: true
        // IncludeNoMutual indicates whether to end animations configured with CanMutualTask: false
        Transition.Exit(Rec0, IncludeMutual: true, IncludeNoMutual: true);
        Transition.Exit(Rec1, IncludeMutual: true, IncludeNoMutual: true);
        Transition.Exit(Rec2, IncludeMutual: true, IncludeNoMutual: true);
    }
}

public partial class MainWindow
{
    // Apply snapshot values synchronously (bypassing the async Execute pipeline so Transform/3D
    // resets are deterministic and reliable).
    // Projection and RenderTransform(Scale) are mutually exclusive on some platforms — clear
    // Projection first, then write the rest.
    private static void ApplyReset(Transition<Rectangle> snapshot, Rectangle target)
    {
        // Two passes: clear Projection first (releasing the mutual exclusion with
        // RenderTransform/Scale), then write everything else.
        // A single property conflict (framework constraint) does not abort the whole reset.
        foreach (var kvp in snapshot.GetState().Values)
            if (kvp.Key.Path.Contains("Projection"))
                try { kvp.Key.SetValue(target, kvp.Value); } catch { }
        foreach (var kvp in snapshot.GetState().Values)
            if (!kvp.Key.Path.Contains("Projection"))
                try { kvp.Key.SetValue(target, kvp.Value); } catch { }
    }

    // Rec0's RenderTransform.X / Fill are modified in place, so reset must use a new object
    private static Transition<Rectangle> CreateResetRec0()
    {
        return Transition<Rectangle>.Create()
            .Property(r => r.RenderTransform, [new TranslateTransform()])
            .Property(r => r.Fill, new SolidColorBrush(Colors.Cyan))
            .Property(r => r.Opacity, 1d)
            .Effect(TransitionEffects.Empty);
    }

    private static Transition<Rectangle> CreateResetRec1()
    {
        return Transition<Rectangle>.Create()
            .Property(r => r.Fill, new SolidColorBrush(Colors.Lime))
            .Property(r => r.Opacity, 1d)
            .Effect(TransitionEffects.Empty);
    }

    private static Transition<Rectangle> CreateResetRec2()
    {
        return Transition<Rectangle>.Create()
            .Property(r => r.Fill, CreateBs1Brush())
            .Property(r => r.Opacity, 1d)
            .Effect(TransitionEffects.Empty);
    }

    // The Bs1 resource of MainWindow.xaml (Yellow → Violet, 0,0 → 1,1), rebuilt in code because a resource is not
    // a value a transition path can point at. Keep the two in step: Rec2's reset animates Fill back to this brush,
    // so editing the XAML alone would leave the reset animating to a stale one.
    private static LinearGradientBrush CreateBs1Brush()
    {
        return new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop(Colors.Yellow, 0),
                new GradientStop(Colors.Violet, 1)
            }
        };
    }

    // Simple animation: demonstrates a nested property path, directly modifying RenderTransform.X
    private static readonly Transition<Rectangle> Animation0 =
        Transition<Rectangle>.Create()
            .Property(r => r.Opacity, 0)
            .Property(r => ((TranslateTransform)r.RenderTransform).X, 800)
            .Property(r => r.Fill, new SolidColorBrush(Colors.Orange))
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(2),
                IsAutoReverse = true,
                LoopTime = 2,
            });

    // Delayed animation: reverse rotation
    private static readonly Transition<Rectangle> Animation1 =
        Transition<Rectangle>.Create()
            .Await(TimeSpan.FromSeconds(5))
            .Property(r => r.RenderTransform, [new RotateTransform(180)], RotationDirection.CounterClockWise)
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(2),
                IsAutoReverse = true,
                LoopTime = 2,
            });

    // Combined animation
    private static readonly Transition<Rectangle> Animation2 =
        Transition<Rectangle>.Create()
            .Property(r => r.RenderTransform,
            [
                new TranslateTransform(200, 0),
                new ScaleTransform(1.3, 1.3)
            ])
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(2),
                IsAutoReverse = true,
                FPS = 144,
                Ease = Eases.Circ.InOut,
                LoopTime = 2,
            })
            .AwaitThen(TimeSpan.FromSeconds(5)) // wait 5 seconds before starting the next animation
            .Property(r => r.Fill, new SolidColorBrush(Colors.Yellow))
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(2),
                Ease = Eases.Sine.In
            });

    // -----------------------------------------------------------------------------------------------
    // Overshoot
    //
    // Every ease used above returns a value inside [0,1], so none of them can pass its target and come
    // back. Back peaks at 1.10 and Elastic at 1.37, and what an overshoot means is decided per sampler:
    // R/G/B move by one shared progress and stop at the first channel to reach its limit, width and
    // height do the same at zero, and a channel with a range of its own — alpha, opacity — keeps the
    // full eased time. The readout is what makes it observable: the number passes the target and
    // returns, which the eye alone cannot tell apart from a slower ease.
    // -----------------------------------------------------------------------------------------------

    private const double ShiftTarget = 300d;
    private const double WidthTarget = 220d;

    private static readonly Transition<Rectangle> OverScalarBack =
        Transition<Rectangle>.Create()
            .Property(r => ((TranslateTransform)r.RenderTransform).X, ShiftTarget)
            .Effect(new TransitionEffect() { Duration = TimeSpan.FromSeconds(0.9), Ease = Eases.Back.Out });

    private static readonly Transition<Rectangle> OverScalarElastic =
        Transition<Rectangle>.Create()
            .Property(r => ((TranslateTransform)r.RenderTransform).X, ShiftTarget)
            .Effect(new TransitionEffect() { Duration = TimeSpan.FromSeconds(1.1), Ease = Eases.Elastic.Out });

    // The target is deliberately mid-range on every channel: with headroom left, the shared progress lets the
    // colour overshoot and still stops at the first channel to reach 255, so what moves is the brightness and not
    // the hue. A per-channel clamp would let green and blue run past red and shift it.
    private static readonly Transition<Rectangle> OverColor =
        Transition<Rectangle>.Create()
            .Property(r => r.Fill, new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0xD0)))
            .Effect(new TransitionEffect() { Duration = TimeSpan.FromSeconds(0.9), Ease = Eases.Back.Out });

    // Width is bounded at zero, so an overshoot that would drive it negative stops at the limit instead of reaching
    // a Size constructor, which throws on a negative size.
    private static readonly Transition<Rectangle> OverSize =
        Transition<Rectangle>.Create()
            .Property(r => r.Width, WidthTarget)
            .Effect(new TransitionEffect() { Duration = TimeSpan.FromSeconds(1.1), Ease = Eases.Elastic.Out });

    // A non-solid Fill goes through the blended-brush path rather than the solid one: a cross-fade, whose factor is
    // a fraction and therefore stops at either end instead of overshooting.
    private static readonly Transition<Rectangle> OverBrush =
        Transition<Rectangle>.Create()
            .Property(r => r.Fill, CreateShiftedBs1())
            .Effect(new TransitionEffect() { Duration = TimeSpan.FromSeconds(1.1), Ease = Eases.Back.Out });

    private static LinearGradientBrush CreateShiftedBs1()
    {
        return new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop(Colors.Cyan, 0.25),
                new GradientStop(Colors.Orange, 0.75)
            }
        };
    }

    private void OverShootScalarBack(object sender, RoutedEventArgs e) => OverScalarBack.Execute(Over0);
    private void OverShootScalarElastic(object sender, RoutedEventArgs e) => OverScalarElastic.Execute(Over0);
    private void OverShootColor(object sender, RoutedEventArgs e) => OverColor.Execute(Over1);
    private void OverShootSize(object sender, RoutedEventArgs e) => OverSize.Execute(Over2);
    private void OverShootGradient(object sender, RoutedEventArgs e) => OverBrush.Execute(Over3);
    private void OverShootReset(object sender, RoutedEventArgs e) => ResetOverShoot();

    private void ResetOverShoot()
    {
        foreach (var target in new[] { Over0, Over1, Over2, Over3 })
            Transition.Exit(target, IncludeMutual: true, IncludeNoMutual: true);

        // Written directly, like the reset above: these are the values the XAML declares.
        Over0.RenderTransform = new TranslateTransform();
        Over1.Fill = new SolidColorBrush(Color.FromRgb(0x3A, 0x6E, 0xA5));
        Over2.Width = 80;
        Over3.Fill = CreateBs1Brush();
    }
}