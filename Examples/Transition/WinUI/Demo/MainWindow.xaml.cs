using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Threading.Tasks;
using VeloxDev.TransitionSystem;
using Windows.Foundation;

namespace Demo
{
    public sealed partial class MainWindow : Window
    {
        private bool _resetInitialized;

        public MainWindow()
        {
            InitializeComponent();

            Rec0.RenderTransform = CreateRec0Transform();
            Rec0.Fill = CreateRec0Brush();

            // The animation targets a DependencyObject; the library marshals directly to its owning
            // UI thread via its DispatcherQueue, so no capture is needed even when the animation is
            // first started from a background thread (Task.Run) below.

            // The core concept of VeloxDev animations is "everything is state"
            // Snapshot(...) records the explicitly specified property paths
            // SnapshotAll() auto-discovers and records all animatable properties of the current object

            // Reset snapshots are taken only after the window is Activated, avoiding information loss
            // from an initial state that has not yet been established.
            // Rec0's properties (RenderTransform.X, Fill.StartPoint/EndPoint) are modified in place,
            // which would pollute the references held by the snapshot, so Rec0 is reset with a new
            // object; Rec1/Rec2 Fills are replaced wholesale (not mutated), so the initial snapshots
            // fully restore them.
            ((FrameworkElement)Content).Loaded += (s, e) =>
            {
                if (_resetInitialized) return;
                _resetInitialized = true;

                // Explicitly initialize to a definite state first, then take the snapshot, so it does
                // not capture non-initial 3D/transform state (which is unreliable).
                Rec0.RenderTransform = CreateRec0Transform();
                Rec0.Fill = CreateRec0Brush();
                Rec0.Projection = null;
                Rec1.RenderTransform = null;
                Rec1.Projection = null;
                Rec2.RenderTransform = null;
                Rec2.Projection = null;

                // Rec1/Rec2 Fills are replaced wholesale (not mutated), so the initial snapshots are stable and reusable
                var reset1 = Rec1.SnapshotAll();
                var reset2 = Rec2.SnapshotAll();

                btnReset.Click += (s, e) =>
                {
                    Transition.Exit(Rec0, IncludeMutual: true, IncludeNoMutual: true);
                    Transition.Exit(Rec1, IncludeMutual: true, IncludeNoMutual: true);
                    Transition.Exit(Rec2, IncludeMutual: true, IncludeNoMutual: true);

                    // Explicitly clear Projection / RenderTransform first to release WinUI's mutual
                    // exclusion constraint (Scale and Projection cannot coexist), otherwise writing
                    // RenderTransform(Scale) while the element still has a Projection throws
                    // UnauthorizedAccessException.
                    Rec0.Projection = null; Rec0.RenderTransform = null;
                    Rec1.Projection = null; Rec1.RenderTransform = null;
                    Rec2.Projection = null; Rec2.RenderTransform = null;

                    // Apply the initial snapshots synchronously: bypasses the async Execute pipeline
                    // (unreliable for Transform reset on some platforms), and Rec0 gets a fresh object
                    // each time so its snapshot references are not polluted by in-place animation edits.
                    ApplyReset(CreateRec0Reset(), Rec0);
                    ApplyReset(reset1, Rec1);
                    ApplyReset(reset2, Rec2);
                };
            };
        }

        private void LoadMainThread(object sender, RoutedEventArgs e)
        {
            // Start directly on the main (UI) thread; mutual exclusion (CanMutualTask: true by default)
            Animation0.Execute(Rec0);
            Animation1.Execute(Rec1);
            Animation2.Execute(Rec2);
        }

        private void LoadAnimations(object sender, RoutedEventArgs e)
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

        private void ExitAnimations(object sender, RoutedEventArgs e)
        {
            // End animations held by the object
            // IncludeMutual   indicates whether to end animations configured with CanMutualTask: true
            // IncludeNoMutual indicates whether to end animations configured with CanMutualTask: false

            Transition.Exit(Rec0, IncludeMutual: true, IncludeNoMutual: true);
            Transition.Exit(Rec1, IncludeMutual: true, IncludeNoMutual: true);
            Transition.Exit(Rec2, IncludeMutual: true, IncludeNoMutual: true);

            // Of course, you can also find an animation's Scheduler via the methods provided by the core library
            // The Scheduler object has Execute() and Exit() capabilities

            //if (TransitionSchedulerCore.TryGetMutualScheduler(Rec0, out var MutualScheduler) &&
            //   TransitionSchedulerCore.TryGetNoMutualScheduler(Rec0, out var noMutualSchedulers))
            //{
            //    ITransitionSchedulerCore[] schedulers = [MutualScheduler!, .. noMutualSchedulers];
            //    foreach (var scheduler in schedulers)
            //    {
            //        scheduler.Exit();
            //    }
            //}
        }

        private void LoadAnimationsNonMutual(object sender, RoutedEventArgs e)
        {
            // CanMutualTask: false — the three animations run concurrently without interference and
            // are not cancelled by one another
            _ = Task.Run(() =>
            {
                Animation0.Execute(Rec0, CanMutualTask: false);
                Animation1.Execute(Rec1, CanMutualTask: false);
                Animation2.Execute(Rec2, CanMutualTask: false);
            });
        }

        private void LoadRepeatedMutual(object sender, RoutedEventArgs e)
        {
            // Each click starts a mutually-exclusive animation on Rec0: the new animation cancels the
            // previous one (tests scheduler gating and cancellation).
            _ = Task.Run(() =>
            {
                Animation0.Execute(Rec0); // CanMutualTask: true (default)
            });
        }
    }

    public sealed partial class MainWindow
    {
        // ⚠ Creating Transition<> in WinUI requires special care:
        //
        // A static Transition<> field used on a non-UI thread may throw exceptions such as TypeInitialization
        // If you want it to be static, make sure the field is instantiated on the UI thread

        // Simple animation: demonstrates a nested property path, directly modifying RenderTransform.X,
        // together with gradient changes.
        private readonly Transition<Rectangle>.StateSnapshot Animation0 =
            Transition<Rectangle>.Create()
                .Property(r => ((TranslateTransform)r.RenderTransform).X, 400)
                .Property(r => ((LinearGradientBrush)r.Fill).StartPoint, new Point(0, 1))
                .Property(r => ((LinearGradientBrush)r.Fill).EndPoint, new Point(1, 1))
                .Effect(new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(1),
                    IsAutoReverse = true,
                    LoopTime = 2
                });

        private static TranslateTransform CreateRec0Transform()
        {
            return new TranslateTransform() { X = 0, Y = 0 };
        }

        private static LinearGradientBrush CreateRec0Brush()
        {
            return new LinearGradientBrush()
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
                GradientStops =
                [
                    new GradientStop() { Color = Colors.Cyan, Offset = 0 },
                    new GradientStop() { Color = Colors.Yellow, Offset = 1 },
                ]
            };
        }

        private static Transition<Rectangle>.StateSnapshot CreateRec0Reset()
        {
            return Transition<Rectangle>.Create()
                .Property(r => r.RenderTransform, [CreateRec0Transform()])
                .Property(r => r.Fill, CreateRec0Brush())
                .Effect(TransitionEffects.Empty);
        }

        // Apply snapshot values synchronously (bypassing the async Execute pipeline so Transform/3D
        // resets are deterministic and reliable).
        // WinUI constraint: Projection and RenderTransform(Scale) are mutually exclusive — clear
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

        // Delayed animation: reverse rotation
        private readonly Transition<Rectangle>.StateSnapshot Animation1 =
            Transition<Rectangle>.Create()
                .Await(TimeSpan.FromSeconds(3))
                .Property(r => r.RenderTransform, [new RotateTransform() { Angle = 180 }], RotationDirection.CounterClockWise)
                .Property(r => r.Fill, new LinearGradientBrush()
                {
                    GradientStops =
                    [
                        new GradientStop(){Color = Colors.Cyan,Offset=0},
                        new GradientStop(){Color= Colors.Red,Offset=0},
                    ]
                })
                .Effect(new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(2),
                    IsAutoReverse = true,
                    LoopTime = 2
                });

        // Combined animation: reverse projection rotation + color change
        private readonly Transition<Rectangle>.StateSnapshot Animation2 =
            Transition<Rectangle>.Create()
                .Property(r => r.Projection,
                    new PlaneProjection()
                    {
                        RotationX = 180,
                        RotationY = 180,
                        CenterOfRotationX = 0.5,
                        CenterOfRotationY = 0.5
                    }, RotationDirection.CounterClockWise)
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
}
