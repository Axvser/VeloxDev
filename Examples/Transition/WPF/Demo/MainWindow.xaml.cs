using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using VeloxDev.TransitionSystem;

namespace Demo;

public partial class MainWindow : Window
{
    private bool _resetInitialized;

    public MainWindow()
    {
        InitializeComponent();

        Rec0.RenderTransform = new TranslateTransform();

        // Reset snapshots are taken only after the element is Loaded, avoiding information loss from
        // an initial state that has not yet been established.
        // Rec0's RenderTransform.X is modified in place, which would pollute the references held by
        // the snapshot, so Rec0 is reset with a new object; Rec1/Rec2 Fills are replaced wholesale
        // (not mutated), so the initial snapshots fully restore them.
        Loaded += (s, e) =>
        {
            if (_resetInitialized) return;
            _resetInitialized = true;

            // Explicitly initialize to a definite state first, then take the snapshot, so it does
            // not capture non-initial transform state (which is unreliable).
            Rec0.RenderTransform = new TranslateTransform();
            Rec1.RenderTransform = null;
            Rec2.RenderTransform = null;

            // Rec1/Rec2 Fills are replaced wholesale (not mutated), so the initial snapshots are stable and reusable
            var reset1 = Rec1.SnapshotAll();
            var reset2 = Rec2.SnapshotAll();

            btnReset.Click += (s, e) =>
            {
                Transition.Exit(Rec0, IncludeMutual: true, IncludeNoMutual: true);
                Transition.Exit(Rec1, IncludeMutual: true, IncludeNoMutual: true);
                Transition.Exit(Rec2, IncludeMutual: true, IncludeNoMutual: true);

                // Apply the initial snapshots synchronously: bypasses the async Execute pipeline
                // (unreliable for Transform reset on some platforms), and Rec0 gets a fresh object
                // each time so its snapshot references are not polluted by in-place animation edits.
                ApplyReset(CreateResetRec0(), Rec0);
                ApplyReset(reset1, Rec1);
                ApplyReset(reset2, Rec2);
            };
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
    private static void ApplyReset(Transition<Rectangle>.StateSnapshot snapshot, Rectangle target)
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
    private static Transition<Rectangle>.StateSnapshot CreateResetRec0()
    {
        return Transition<Rectangle>.Create()
            .Property(r => r.RenderTransform, [new TranslateTransform()])
            .Property(r => r.Fill, new SolidColorBrush(Colors.Cyan))
            .Property(r => r.Opacity, 1d)
            .Effect(TransitionEffects.Empty);
    }

    // Simple animation: demonstrates a nested property path, directly modifying RenderTransform.X
    private static readonly Transition<Rectangle>.StateSnapshot Animation0 =
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
    private static readonly Transition<Rectangle>.StateSnapshot Animation1 =
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
    private static readonly Transition<Rectangle>.StateSnapshot Animation2 =
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
}