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

            // 重置快照在页面出现（OnAppearing）后才拍摄，避免初始状态尚未确立导致信息丢失。
            // Rec1/Rec2 的 Fill 是整体替换（不突变），初始快照稳定可复用。
            var reset1 = Rec1.SnapshotAll();
            var reset2 = Rec2.SnapshotAll();

            btnReset.Clicked += (s, e) =>
            {
                Transition.Exit(Rec0, IncludeMutual: true, IncludeNoMutual: true);
                Transition.Exit(Rec1, IncludeMutual: true, IncludeNoMutual: true);
                Transition.Exit(Rec2, IncludeMutual: true, IncludeNoMutual: true);

                // Rec0 的 Fill.StartPoint/EndPoint 会被动画原地修改，每次重置必须新建对象，避免快照引用被污染
                CreateRec0Reset().Execute(Rec0);
                reset1.Effect(TransitionEffects.Empty).Execute(Rec1);
                reset2.Effect(TransitionEffects.Empty).Execute(Rec2);
            };
        }

        private void LoadMainThread(object sender, EventArgs e)
        {
            // 主线程（UI 线程）直接启动，CanMutualTask: true（默认）——互斥，新动画打断旧动画
            Animation0.Execute(Rec0);
            Animation1.Execute(Rec1);
            Animation2.Execute(Rec2);
        }

        private void LoadBackground(object sender, EventArgs e)
        {
            // 非 UI 线程启动，框架自动切回 UI 线程（测试目标派生的线程编组）
            _ = Task.Run(() =>
            {
                Animation0.Execute(Rec0);
                Animation1.Execute(Rec1);
                Animation2.Execute(Rec2);
            });
        }

        private void LoadMainThreadNonMutual(object sender, EventArgs e)
        {
            // 主线程 + CanMutualTask: false —— 并发运行，互不取消
            Animation0.Execute(Rec0, CanMutualTask: false);
            Animation1.Execute(Rec1, CanMutualTask: false);
            Animation2.Execute(Rec2, CanMutualTask: false);
        }

        private void LoadBackgroundNonMutual(object sender, EventArgs e)
        {
            // 非 UI 线程 + CanMutualTask: false —— 并发运行
            _ = Task.Run(() =>
            {
                Animation0.Execute(Rec0, CanMutualTask: false);
                Animation1.Execute(Rec1, CanMutualTask: false);
                Animation2.Execute(Rec2, CanMutualTask: false);
            });
        }

        private void RepeatMutual(object sender, EventArgs e)
        {
            // 每次点击在 Rec0 上启动互斥动画，新动画取消上一次（测试调度器门控与取消）
            _ = Task.Run(() => Animation0.Execute(Rec0));
        }

        private void ExitAll(object sender, EventArgs e)
        {
            // IncludeMutual   表示是否终结 CanMutualTask: true 的动画
            // IncludeNoMutual 表示是否终结 CanMutualTask: false 的动画
            Transition.Exit(Rec0, IncludeMutual: true, IncludeNoMutual: true);
            Transition.Exit(Rec1, IncludeMutual: true, IncludeNoMutual: true);
            Transition.Exit(Rec2, IncludeMutual: true, IncludeNoMutual: true);
        }
    }

    public partial class MainPage
    {
        // 简单动画：平移 + 演示嵌套属性路径，直接修改 Fill.StartPoint / Fill.EndPoint
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

        // 延迟动画 - 旋转
        private static readonly Transition<Rectangle>.StateSnapshot Animation1 =
            Transition<Rectangle>.Create()
                .Await(TimeSpan.FromSeconds(2))
                .Property(r => r.RotationX, 180)     // MAUI X旋转
                .Effect(new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(2),
                    IsAutoReverse = true,
                    LoopTime = 2,
                });

        // 拼接动画 - 组合变换
        private static readonly Transition<Rectangle>.StateSnapshot Animation2 =
            Transition<Rectangle>.Create()
                // 第一段：平移 + 缩放
                .Property(r => r.RotationX, 180)
                .Property(r => r.RotationY, 180)
                .Property(r => r.TranslationX, 200)
                .Property(r => r.TranslationY, 0)
                .Property(r => r.Scale, 1.3)         // MAUI 的整体缩放
                .Effect(new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(2),
                    IsAutoReverse = true,
                    FPS = 144,
                    Ease = Eases.Circ.InOut,
                    LoopTime = 2,
                })
                .AwaitThen(TimeSpan.FromSeconds(5))
                // 第二段：颜色变化
                .Property(r => r.Fill, new SolidColorBrush(Colors.Yellow))
                .Effect(new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(2),
                    Ease = Eases.Sine.In
                });
    }
}
