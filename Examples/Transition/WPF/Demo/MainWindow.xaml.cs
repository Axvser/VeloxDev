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

        // 重置快照在元素加载完成后（Loaded）才拍摄，避免初始状态尚未确立导致信息丢失。
        // Rec0 的 RenderTransform.X 会被原地修改，快照持有的引用会被污染，
        // 因此 Rec0 用新建对象重置；Rec1/Rec2 的 Fill 是整体替换（不突变），用初始快照即可完整恢复。
        Loaded += (s, e) =>
        {
            if (_resetInitialized) return;
            _resetInitialized = true;

            // 先显式初始化到确定状态，再拍快照，避免快照捕获到非初始的变换（时好时坏）
            Rec0.RenderTransform = new TranslateTransform();
            Rec1.RenderTransform = null;
            Rec2.RenderTransform = null;

            // Rec1/Rec2 的 Fill 是整体替换（不突变），初始快照稳定可复用
            var reset1 = Rec1.SnapshotAll();
            var reset2 = Rec2.SnapshotAll();

            btnReset.Click += (s, e) =>
            {
                Transition.Exit(Rec0, IncludeMutual: true, IncludeNoMutual: true);
                Transition.Exit(Rec1, IncludeMutual: true, IncludeNoMutual: true);
                Transition.Exit(Rec2, IncludeMutual: true, IncludeNoMutual: true);

                // 直接同步应用初始快照：绕过 async Execute 管线（其在部分平台对 Transform 重置不可靠），
                // 且 Rec0 每次新建对象，避免快照引用被动画原地修改污染
                ApplyReset(CreateResetRec0(), Rec0);
                ApplyReset(reset1, Rec1);
                ApplyReset(reset2, Rec2);
            };
        };
    }

    private void LoadMainThread(object sender, RoutedEventArgs e)
    {
        // 主线程（UI 线程）直接启动，CanMutualTask: true（默认）——互斥，新动画打断旧动画
        Animation0.Execute(Rec0);
        Animation1.Execute(Rec1);
        Animation2.Execute(Rec2);
    }

    private void LoadBackground(object sender, RoutedEventArgs e)
    {
        // 非 UI 线程启动，框架自动切回 UI 线程（测试目标派生的线程编组）
        _ = Task.Run(() =>
        {
            Animation0.Execute(Rec0);
            Animation1.Execute(Rec1);
            Animation2.Execute(Rec2);
        });
    }

    private void LoadMainThreadNonMutual(object sender, RoutedEventArgs e)
    {
        // 主线程 + CanMutualTask: false —— 并发运行，互不取消
        Animation0.Execute(Rec0, CanMutualTask: false);
        Animation1.Execute(Rec1, CanMutualTask: false);
        Animation2.Execute(Rec2, CanMutualTask: false);
    }

    private void LoadBackgroundNonMutual(object sender, RoutedEventArgs e)
    {
        // 非 UI 线程 + CanMutualTask: false —— 并发运行
        _ = Task.Run(() =>
        {
            Animation0.Execute(Rec0, CanMutualTask: false);
            Animation1.Execute(Rec1, CanMutualTask: false);
            Animation2.Execute(Rec2, CanMutualTask: false);
        });
    }

    private void RepeatMutual(object sender, RoutedEventArgs e)
    {
        // 每次点击在 Rec0 上启动互斥动画，新动画取消上一次（测试调度器门控与取消）
        _ = Task.Run(() => Animation0.Execute(Rec0));
    }

    private void ExitAll(object sender, RoutedEventArgs e)
    {
        // IncludeMutual   表示是否终结 CanMutualTask: true 的动画
        // IncludeNoMutual 表示是否终结 CanMutualTask: false 的动画
        Transition.Exit(Rec0, IncludeMutual: true, IncludeNoMutual: true);
        Transition.Exit(Rec1, IncludeMutual: true, IncludeNoMutual: true);
        Transition.Exit(Rec2, IncludeMutual: true, IncludeNoMutual: true);
    }
}

public partial class MainWindow
{
    // 直接同步应用快照值（绕过 async Execute 管线，确保 Transform/3D 重置确定可靠）。
    // Projection 与 RenderTransform(Scale) 在部分平台互斥——先清除 Projection，再写其余。
    private static void ApplyReset(Transition<Rectangle>.StateSnapshot snapshot, Rectangle target)
    {
        // 两遍：先清除 Projection（解除与 RenderTransform/Scale 的互斥），再写其余。
        // 单个属性冲突（框架约束）不中断整个重置。
        foreach (var kvp in snapshot.GetState().Values)
            if (kvp.Key.Path.Contains("Projection"))
                try { kvp.Key.SetValue(target, kvp.Value); } catch { }
        foreach (var kvp in snapshot.GetState().Values)
            if (!kvp.Key.Path.Contains("Projection"))
                try { kvp.Key.SetValue(target, kvp.Value); } catch { }
    }

    // Rec0 的 RenderTransform.X / Fill 会被原地修改，重置必须用新建对象
    private static Transition<Rectangle>.StateSnapshot CreateResetRec0()
    {
        return Transition<Rectangle>.Create()
            .Property(r => r.RenderTransform, [new TranslateTransform()])
            .Property(r => r.Fill, new SolidColorBrush(Colors.Cyan))
            .Property(r => r.Opacity, 1d)
            .Effect(TransitionEffects.Empty);
    }

    // 简单动画：演示嵌套属性路径，直接修改 RenderTransform.X
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

    // 延迟动画：反转旋转
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

    // 拼接动画
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
            .AwaitThen(TimeSpan.FromSeconds(5)) // 等待 5秒再开始下一段动画
            .Property(r => r.Fill, new SolidColorBrush(Colors.Yellow))
            .Effect(new TransitionEffect()
            {
                Duration = TimeSpan.FromSeconds(2),
                Ease = Eases.Sine.In
            });
}