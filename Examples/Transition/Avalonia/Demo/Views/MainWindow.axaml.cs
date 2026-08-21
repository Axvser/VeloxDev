using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using System;
using System.Threading.Tasks;
using VeloxDev.TransitionSystem;

namespace Demo.Views;

public partial class MainWindow : Window
{
    private bool _resetInitialized;

    public MainWindow()
    {
        InitializeComponent();

        Rec0.RenderTransform = new TranslateTransform();

        // Reset snapshots are taken only after the window is Opened, avoiding information loss from
        // an initial state that has not yet been established.
        // Rec0's RenderTransform.X / Fill are modified in place, which would pollute the references
        // held by the snapshot, so Rec0 is reset with a new object; Rec1/Rec2 Fills are replaced
        // wholesale (not mutated), so the initial snapshots fully restore them.
        Loaded += (s, e) =>
        {
            if (_resetInitialized) return;
            _resetInitialized = true;

            // Explicitly initialize to a definite state first, then take the snapshot, so it does
            // not capture non-initial 3D/transform state (which is unreliable).
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
            .Effect(TransitionEffects.Empty);
    }

    // Simple animation: demonstrates a nested property path, directly modifying RenderTransform.X
    private static readonly Transition<Rectangle>.StateSnapshot Animation0 =
        Transition<Rectangle>.Create()
            .Property(r => ((TranslateTransform)r.RenderTransform!).X, 400)
            .Property(r => r.Fill,
                new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(new Point(0, 0), RelativeUnit.Relative),
                    EndPoint = new RelativePoint(new Point(1, 0), RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Colors.DeepSkyBlue, 0),
                        new GradientStop(Colors.MediumPurple, 1)
                    }
                })
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(2),
                IsAutoReverse = true,
                LoopTime = 2,
                Ease = Eases.Sine.InOut
            });

    // Delayed animation: reverse rotation + movement + background gradient
    private static readonly Transition<Rectangle>.StateSnapshot Animation1 =
        Transition<Rectangle>.Create()
            .Await(TimeSpan.FromSeconds(5))
            .Property(r => r.RenderTransform, [new TranslateTransform(-200, 0), new RotateTransform(180)], RotationDirection.ClockWise)
            .Property(r => r.Fill,
                new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(new Point(0, 0), RelativeUnit.Relative),
                    EndPoint = new RelativePoint(new Point(0, 1), RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Colors.OrangeRed, 0),
                        new GradientStop(Colors.Yellow, 1)
                    }
                })
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(4),
                IsAutoReverse = true,
                FPS = 144,
                LoopTime = 4,
            });

    // Combined animation: reverse 3D rotation + scaling + switch to a new gradient background
    private static readonly Transition<Rectangle>.StateSnapshot Animation2 =
        Transition<Rectangle>.Create()
            .Property(r => r.RenderTransform,
            [
                new TranslateTransform(200, 0),
                new Rotate3DTransform(180, 180, 0, 0, 0, 0, 0),
                new ScaleTransform(1.3, 1.3)
            ], RotationDirection.CounterClockWise)
            .Property(r => r.Fill,
                new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(new Point(0, 0), RelativeUnit.Relative),
                    EndPoint = new RelativePoint(new Point(1, 1), RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Colors.LightSeaGreen, 0),
                        new GradientStop(Colors.CadetBlue, 1)
                    }
                })
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(3),
                IsAutoReverse = true,
                FPS = 144,
                Ease = Eases.Circ.InOut,
                LoopTime = 2,
            })
            .AwaitThen(TimeSpan.FromSeconds(5))
            .Property(r => r.Fill,
                new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(new Point(1, 0), RelativeUnit.Relative),
                    EndPoint = new RelativePoint(new Point(0, 1), RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Colors.Yellow, 0),
                        new GradientStop(Colors.Lime, 1)
                    }
                })
            .Effect((x) =>
            {
                x.Duration = TimeSpan.FromSeconds(4);
                x.FPS = 144;
                x.Ease = Eases.Sine.In;
            });
}