using Microsoft.Maui.Controls.Shapes;
using VeloxDev.TransitionSystem;

namespace Demo
{
    public partial class MainPage : ContentPage
    {
        private bool _resetInitialized;

        public MainPage()
        {
            InitializeComponent();

            Rec0.Fill = CreateRec0Brush();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            if (_resetInitialized) return;
            _resetInitialized = true;

            // Explicitly initialize to a definite state first, then take the snapshot, so it does
            // not capture non-initial 3D/transform state (which is unreliable).
            Rec0.Fill = CreateRec0Brush();
            Rec0.RotationX = 0; Rec0.RotationY = 0; Rec0.Scale = 1; Rec0.TranslationX = 0; Rec0.TranslationY = 0;
            Rec1.RotationX = 0; Rec1.RotationY = 0; Rec1.Scale = 1; Rec1.TranslationX = 0; Rec1.TranslationY = 0;
            Rec2.RotationX = 0; Rec2.RotationY = 0; Rec2.Scale = 1; Rec2.TranslationX = 0; Rec2.TranslationY = 0;

            // Rec1/Rec2 Fills are replaced wholesale (not mutated), so the initial snapshots are stable and reusable.
            var reset1 = Rec1.SnapshotAll();
            var reset2 = Rec2.SnapshotAll();

            btnReset.Clicked += (s, e) =>
            {
                Transition.Exit(Rec0, IncludeMutual: true, IncludeNoMutual: true);
                Transition.Exit(Rec1, IncludeMutual: true, IncludeNoMutual: true);
                Transition.Exit(Rec2, IncludeMutual: true, IncludeNoMutual: true);

                // Apply the initial snapshots synchronously: bypasses the async Execute pipeline
                // (unreliable for Transform reset on some platforms), and Rec0 gets a fresh object
                // each time so its snapshot references are not polluted by in-place animation edits.
                ApplyReset(CreateRec0Reset(), Rec0);
                ApplyReset(reset1, Rec1);
                ApplyReset(reset2, Rec2);
            };
        }

        private void LoadMainThread(object sender, EventArgs e)
        {
            // Start directly on the main (UI) thread; CanMutualTask: true (default) — mutually
            // exclusive, the new animation interrupts the old one
            Animation0.Execute(Rec0);
            Animation1.Execute(Rec1);
            Animation2.Execute(Rec2);
        }

        private void LoadBackground(object sender, EventArgs e)
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

        private void LoadMainThreadNonMutual(object sender, EventArgs e)
        {
            // Main thread + CanMutualTask: false — run concurrently, neither cancels the other
            Animation0.Execute(Rec0, CanMutualTask: false);
            Animation1.Execute(Rec1, CanMutualTask: false);
            Animation2.Execute(Rec2, CanMutualTask: false);
        }

        private void LoadBackgroundNonMutual(object sender, EventArgs e)
        {
            // Non-UI thread + CanMutualTask: false — run concurrently
            _ = Task.Run(() =>
            {
                Animation0.Execute(Rec0, CanMutualTask: false);
                Animation1.Execute(Rec1, CanMutualTask: false);
                Animation2.Execute(Rec2, CanMutualTask: false);
            });
        }

        private void RepeatMutual(object sender, EventArgs e)
        {
            // Each click starts a mutually-exclusive animation on Rec0, and the new animation cancels
            // the previous one (tests scheduler gating and cancellation).
            _ = Task.Run(() => Animation0.Execute(Rec0));
        }

        private void ExitAll(object sender, EventArgs e)
        {
            // IncludeMutual   indicates whether to end animations configured with CanMutualTask: true
            // IncludeNoMutual indicates whether to end animations configured with CanMutualTask: false
            Transition.Exit(Rec0, IncludeMutual: true, IncludeNoMutual: true);
            Transition.Exit(Rec1, IncludeMutual: true, IncludeNoMutual: true);
            Transition.Exit(Rec2, IncludeMutual: true, IncludeNoMutual: true);
        }
    }

    public partial class MainPage
    {
        // Simple animation: translate + demonstrates a nested property path, directly modifying
        // Fill.StartPoint / Fill.EndPoint
        private static readonly Transition<Rectangle>.StateSnapshot Animation0 =
            Transition<Rectangle>.Create()
                .Property(r => r.TranslationX, 240)
                .Property(r => ((LinearGradientBrush)r.Fill!).StartPoint, new Point(0, 1))
                .Property(r => ((LinearGradientBrush)r.Fill!).EndPoint, new Point(1, 1))
                .Effect(new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(2),
                    IsAutoReverse = true,
                    LoopTime = 2,
                });

        private static LinearGradientBrush CreateRec0Brush()
        {
            return new LinearGradientBrush()
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
                GradientStops =
                [
                    new GradientStop(Colors.Cyan, 0),
                    new GradientStop(Colors.Yellow, 1)
                ]
            };
        }

        private static Transition<Rectangle>.StateSnapshot CreateRec0Reset()
        {
            return Transition<Rectangle>.Create()
                .Property(r => r.TranslationX, 0)
                .Property(r => r.Fill, CreateRec0Brush())
                .Effect(TransitionEffects.Empty);
        }

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

        // Delayed animation - rotation
        private static readonly Transition<Rectangle>.StateSnapshot Animation1 =
            Transition<Rectangle>.Create()
                .Await(TimeSpan.FromSeconds(2))
                .Property(r => r.RotationX, 180)     // MAUI X rotation
                .Effect(new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(2),
                    IsAutoReverse = true,
                    LoopTime = 2,
                });

        // Combined animation - composite transforms
        private static readonly Transition<Rectangle>.StateSnapshot Animation2 =
            Transition<Rectangle>.Create()
                // First segment: translate + scale
                .Property(r => r.RotationX, 180)
                .Property(r => r.RotationY, 180)
                .Property(r => r.TranslationX, 200)
                .Property(r => r.TranslationY, 0)
                .Property(r => r.Scale, 1.3)         // MAUI overall scale
                .Effect(new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(2),
                    IsAutoReverse = true,
                    FPS = 144,
                    Ease = Eases.Circ.InOut,
                    LoopTime = 2,
                })
                .AwaitThen(TimeSpan.FromSeconds(5))
                // Second segment: color change
                .Property(r => r.Fill, new SolidColorBrush(Colors.Yellow))
                .Effect(new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(2),
                    Ease = Eases.Sine.In
                });
    }
}
