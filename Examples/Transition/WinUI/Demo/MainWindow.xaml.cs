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

            // The overshoot strip drives RenderTransform.X in place, so its transform is created once here.
            Over0.RenderTransform = new TranslateTransform();

            // The animation targets a DependencyObject; the library marshals directly to its owning
            // UI thread via its DispatcherQueue, so no capture is needed even when the animation is
            // first started from a background thread (Task.Run) below.

            // The core concept of VeloxDev animations is "everything is state": a transition is a set of
            // explicitly specified property paths, built with Transition<T>.Create().Property(...).

            // The reset is wired only after the window is Activated, so it describes an established initial state.
            // Rec0's properties (RenderTransform.X, Fill.StartPoint/EndPoint) are modified in place, so Rec0 is
            // reset with a new object.
            ((FrameworkElement)Content).Loaded += (s, e) =>
            {
                if (_resetInitialized) return;
                _resetInitialized = true;

                // Explicitly initialize to a definite state first, so the reset paths below describe a known state.
                Rec0.RenderTransform = CreateRec0Transform();
                Rec0.Fill = CreateRec0Brush();
                Rec0.Projection = null;
                Rec1.RenderTransform = null;
                Rec1.Projection = null;
                Rec2.RenderTransform = null;
                Rec2.Projection = null;

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

                    // Apply the reset synchronously: bypasses the async Execute pipeline
                    // (unreliable for Transform reset on some platforms), and Rec0 gets a fresh object
                    // each time so its references are not polluted by in-place animation edits.
                    ApplyReset(CreateRec0Reset(), Rec0);
                    ApplyReset(CreateRec1Reset(), Rec1);
                    ApplyReset(CreateRec2Reset(), Rec2);

                    ResetOverShoot();
                };

                // Readout for the overshoot strip. Sampled on a timer rather than from the effect's events: the
                // pipeline clones the effect once per segment, so the handlers subscribed here are not the ones
                // that fire.
                var readout = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()!.CreateTimer();
                readout.Interval = TimeSpan.FromMilliseconds(40);
                readout.Tick += (s, e) => UpdateReadout();
                readout.Start();
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
        private readonly Transition<Rectangle> Animation0 =
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

        private static Transition<Rectangle> CreateRec0Reset()
        {
            return Transition<Rectangle>.Create()
                .Property(r => r.RenderTransform, [CreateRec0Transform()])
                .Property(r => r.Fill, CreateRec0Brush())
                .Effect(TransitionEffects.Empty);
        }

        private static Transition<Rectangle> CreateRec1Reset()
        {
            return Transition<Rectangle>.Create()
                .Property(r => r.Fill, new SolidColorBrush(Colors.Lime))
                .Effect(TransitionEffects.Empty);
        }

        private static Transition<Rectangle> CreateRec2Reset()
        {
            return Transition<Rectangle>.Create()
                .Property(r => r.Fill, CreateBs1Brush())
                .Effect(TransitionEffects.Empty);
        }

        // Rec2's brush as declared inline in MainWindow.xaml (Yellow → Violet, 0,0 → 1,1), rebuilt in code. Keep the
        // two in step: Rec2's reset animates Fill back to this brush, so editing the XAML alone would leave the
        // reset animating to a stale one.
        private static LinearGradientBrush CreateBs1Brush()
        {
            return new LinearGradientBrush()
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                [
                    new GradientStop() { Color = Colors.Yellow, Offset = 0 },
                    new GradientStop() { Color = Colors.Violet, Offset = 1 },
                ]
            };
        }

        // Apply snapshot values synchronously (bypassing the async Execute pipeline so Transform/3D
        // resets are deterministic and reliable).
        // WinUI constraint: Projection and RenderTransform(Scale) are mutually exclusive — clear
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

        // Delayed animation: reverse rotation
        private readonly Transition<Rectangle> Animation1 =
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
        private readonly Transition<Rectangle> Animation2 =
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

        // -----------------------------------------------------------------------------------------------
        // 过冲
        //
        // 上面用到的缓动全都落在 [0,1] 内，没有一条能越过目标再回来。Back 峰值 1.10、Elastic 峰值 1.37，
        // 而"过冲"到底意味着什么由各采样器自己决定：R/G/B 共用一个进度、在第一个触限的通道处停住，
        // 宽/高同理在边界处停住，而自带值域的单通道（alpha、opacity）保留完整的缓动时间。
        // 读数是过冲能被观察到的关键：数字越过目标再回来，肉眼分辨不出这和一个更慢的缓动。
        // -----------------------------------------------------------------------------------------------

        private const double ShiftTarget = 300d;
        private const double WidthTarget = 220d;
        private const double BarWidth = 80d;

        // 初始色与目标色都留在中段，各通道都有余量：共享进度让颜色能过冲，又会在第一个触限的通道上停住，
        // 于是变的是亮度而不是色相。注意 WinUI 的刷子采样器走预乘 alpha 混合，颜色的过冲进度取自
        // 停靠色本身而不是预乘后的通道——每个预乘通道里都带着 alpha，在那种空间里色相无从约束。
        private static readonly Windows.UI.Color ColorStart = Windows.UI.Color.FromArgb(255, 0x3A, 0x6E, 0xA5);
        private static readonly Windows.UI.Color ColorTarget = Windows.UI.Color.FromArgb(255, 0x80, 0x80, 0xD0);

        // 位移 Back：读数在 300 之前越过 300，再回落到 300。
        private readonly Transition<Rectangle> OverScalarBack =
            Transition<Rectangle>.Create()
                .Property(r => ((TranslateTransform)r.RenderTransform).X, ShiftTarget)
                .Effect(new TransitionEffect() { Duration = TimeSpan.FromSeconds(0.9), Ease = Eases.Back.Out });

        // 位移 Elastic：越过幅度更大（峰值约 1.37 倍），回落次数也更多。
        private readonly Transition<Rectangle> OverScalarElastic =
            Transition<Rectangle>.Create()
                .Property(r => ((TranslateTransform)r.RenderTransform).X, ShiftTarget)
                .Effect(new TransitionEffect() { Duration = TimeSpan.FromSeconds(1.1), Ease = Eases.Elastic.Out });

        // 颜色：过冲不能回绕（回绕会得到完全错误的颜色），也不能让某个通道单独跑出去（那会偏移色相）。
        private readonly Transition<Rectangle> OverColor =
            Transition<Rectangle>.Create()
                .Property(r => r.Fill, new SolidColorBrush(ColorTarget))
                .Effect(new TransitionEffect() { Duration = TimeSpan.FromSeconds(0.9), Ease = Eases.Back.Out });

        // 尺寸：从 80 涨到 220，弹性回落的极小值约 0.98 倍，宽度始终落在零以上。
        // 注意 double 类型的宽度并没有"零边界"：采样器把缓动值原样透传，朝下的过冲会把宽度压成负值，
        // 而 WinUI 的 Width setter 会直接抛异常。所以这一步的目标值只朝上走，避免跌到零以下。
        private readonly Transition<Rectangle> OverSize =
            Transition<Rectangle>.Create()
                .Property(r => r.Width, WidthTarget)
                .Effect(new TransitionEffect() { Duration = TimeSpan.FromSeconds(1.1), Ease = Eases.Elastic.Out });

        // 非纯色刷走的是混合刷子路径而不是纯色路径：交叉淡出系数与停靠点共享一个进度，在端点饱和而不是越界。
        private readonly Transition<Rectangle> OverBrush =
            Transition<Rectangle>.Create()
                .Property(r => r.Fill, CreateShiftedBs1())
                .Effect(new TransitionEffect() { Duration = TimeSpan.FromSeconds(1.1), Ease = Eases.Back.Out });

        private static LinearGradientBrush CreateShiftedBs1()
        {
            return new LinearGradientBrush()
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                [
                    new GradientStop() { Color = Colors.Cyan, Offset = 0.25 },
                    new GradientStop() { Color = Colors.Orange, Offset = 0.75 },
                ]
            };
        }

        // 每次运行前先把自己那个目标同步地放回起始值（而不是用一次动画退回去）：Prepare 把目标的当前值当作
        // 动画起点，少了这一步，同一条按钮的第二次点击、或共用该元素的另一条按钮，就会从目标动画到目标，
        // 看起来什么都没发生——真机验收时这就等于"功能是坏的"。
        // 只重置本处理器自己的元素：重置整条过冲条会取消别的元素上正在跑的那一次，并排对比也就没了。
        private void OverShootScalarBack(object sender, RoutedEventArgs e) => RunScalar(OverScalarBack);
        private void OverShootScalarElastic(object sender, RoutedEventArgs e) => RunScalar(OverScalarElastic);

        private void RunScalar(Transition<Rectangle> animation)
        {
            Transition.Exit(Over0, IncludeMutual: true, IncludeNoMutual: true);
            ((TranslateTransform)Over0.RenderTransform).X = 0;
            animation.Execute(Over0);
        }

        private void OverShootColor(object sender, RoutedEventArgs e)
        {
            Transition.Exit(Over1, IncludeMutual: true, IncludeNoMutual: true);
            Over1.Fill = new SolidColorBrush(ColorStart);
            OverColor.Execute(Over1);
        }

        private void OverShootSize(object sender, RoutedEventArgs e)
        {
            Transition.Exit(Over2, IncludeMutual: true, IncludeNoMutual: true);
            Over2.Width = BarWidth;
            OverSize.Execute(Over2);
        }

        private void OverShootGradient(object sender, RoutedEventArgs e)
        {
            Transition.Exit(Over3, IncludeMutual: true, IncludeNoMutual: true);
            Over3.Fill = CreateBs1Brush();
            OverBrush.Execute(Over3);
        }

        private void OverShootReset(object sender, RoutedEventArgs e) => ResetOverShoot();

        private void ResetOverShoot()
        {
            foreach (var target in new[] { Over0, Over1, Over2, Over3 })
                Transition.Exit(target, IncludeMutual: true, IncludeNoMutual: true);

            // Written directly, like the reset above: these are the values the XAML declares.
            Over0.RenderTransform = new TranslateTransform();
            Over1.Fill = new SolidColorBrush(ColorStart);
            Over2.Width = BarWidth;
            Over3.Fill = CreateBs1Brush();
        }

        // 读数只取目标的真实属性，不缓存也不伪造。
        private void UpdateReadout()
        {
            var shift = Over0.RenderTransform is TranslateTransform t ? t.X : double.NaN;
            var fill = Over1.Fill is SolidColorBrush b
                ? $"#{b.Color.R:X2}{b.Color.G:X2}{b.Color.B:X2}"
                : Over1.Fill?.GetType().Name ?? "无";

            Readout.Text =
                $"位移 X   目标 {ShiftTarget,6:F1}   当前 {shift,7:F1}"
                + $"     |     宽度   目标 {WidthTarget,6:F1}   当前 {Over2.Width,7:F1}"
                + $"     |     填充   {fill}";
        }
    }
}
