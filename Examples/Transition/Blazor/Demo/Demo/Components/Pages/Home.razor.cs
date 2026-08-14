using Demo.Models;
using Microsoft.AspNetCore.Components;
using VeloxDev.TransitionSystem;

namespace Demo.Components.Pages;

public partial class Home : ComponentBase, IDisposable
{
    // ---------------------------------------------------------------
    // ViewModel 实例 — 动画直接操作这些对象的属性
    // ---------------------------------------------------------------
    private BoxModel Box0 { get; } = new() { Color = "#00bcd4" };
    private BoxModel Box1 { get; } = new() { Color = "#66bb6a" };
    private BoxModel Box2 { get; } = new() { Color = "#ab47bc" };

    // ---------------------------------------------------------------
    // 动画定义（对标 WPF/Avalonia Demo 的三段动画）
    // ---------------------------------------------------------------

    // Animation0：简单动画 — 位移 + 颜色 + 透明度，自动往返循环
    private static readonly Transition<BoxModel>.StateSnapshot Animation0 =
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

    // Animation1：延迟动画 — 等待 2 秒后旋转 + 缩放
    private static readonly Transition<BoxModel>.StateSnapshot Animation1 =
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

    // Animation2：拼接动画 — 先向右移动，等待 3s 后再变色 + 缩小
    private static readonly Transition<BoxModel>.StateSnapshot Animation2 =
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

    // ---------------------------------------------------------------
    // 初始快照（用于 Reset）
    // ---------------------------------------------------------------
    private Transition<BoxModel>.StateSnapshot _snapshot0 = default!;
    private Transition<BoxModel>.StateSnapshot _snapshot1 = default!;
    private Transition<BoxModel>.StateSnapshot _snapshot2 = default!;

    protected override void OnInitialized()
    {
        // Blazor 的动画目标是 POCO ViewModel，没有 dispatcher 亲和，后台线程无法反推 circuit
        // 上下文，因此必须在此（OnInitialized，circuit 线程）捕获一次。这是 Blazor 模型的固有限制。
        UIThreadInspector.CaptureUIThread();

        // 订阅属性变更，驱动 Blazor 重渲染
        Box0.PropertyChanged += (_, _) => InvokeAsync(StateHasChanged);
        Box1.PropertyChanged += (_, _) => InvokeAsync(StateHasChanged);
        Box2.PropertyChanged += (_, _) => InvokeAsync(StateHasChanged);
    }

    protected override void OnAfterRender(bool firstRender)
    {
        // 首次渲染完成后才拍摄重置快照，确保初始状态完整、不丢失信息
        if (!firstRender) return;

        _snapshot0 = Box0.SnapshotAll();
        _snapshot1 = Box1.SnapshotAll();
        _snapshot2 = Box2.SnapshotAll();
    }

    private void LoadMainThread()
    {
        // 主线程（circuit 线程）直接启动，互斥（CanMutualTask: true 默认）
        Animation0.Execute(Box0);
        Animation1.Execute(Box1);
        Animation2.Execute(Box2);
    }

    private void LoadAnimations()
    {
        // 也可以在非 UI 线程中启动，框架会自动切换
        _ = Task.Run(() =>
        {
            Animation0.Execute(Box0);
            Animation1.Execute(Box1);
            Animation2.Execute(Box2);
        });
    }

    private void LoadMainThreadNonMutual()
    {
        // 主线程 + CanMutualTask: false —— 并发运行，互不取消
        Animation0.Execute(Box0, CanMutualTask: false);
        Animation1.Execute(Box1, CanMutualTask: false);
        Animation2.Execute(Box2, CanMutualTask: false);
    }

    private void LoadAnimationsNonMutual()
    {
        // CanMutualTask: false —— 三段动画互不干扰地并发运行，不会被彼此取消
        _ = Task.Run(() =>
        {
            Animation0.Execute(Box0, CanMutualTask: false);
            Animation1.Execute(Box1, CanMutualTask: false);
            Animation2.Execute(Box2, CanMutualTask: false);
        });
    }

    private void LoadRepeatedMutual()
    {
        // 每次点击在 Box0 上启动互斥动画：新动画会取消上一次（测试调度器门控与取消）
        _ = Task.Run(() => Animation0.Execute(Box0));
    }

    private void ResetBox0()
    {
        // 重置全部：以零时长过渡立即恢复三个 Box 到快照记录的初始状态
        Transition.Exit(Box0, IncludeMutual: true, IncludeNoMutual: true);
        Transition.Exit(Box1, IncludeMutual: true, IncludeNoMutual: true);
        Transition.Exit(Box2, IncludeMutual: true, IncludeNoMutual: true);

        _snapshot0.Effect(TransitionEffects.Empty).Execute(Box0);
        _snapshot1.Effect(TransitionEffects.Empty).Execute(Box1);
        _snapshot2.Effect(TransitionEffects.Empty).Execute(Box2);
    }

    private void ExitAnimations()
    {
        // IncludeMutual   表示是否终结 CanMutualTask: true 的动画
        // IncludeNoMutual 表示是否终结 CanMutualTask: false 的动画
        Transition.Exit(Box0, IncludeMutual: true, IncludeNoMutual: true);
        Transition.Exit(Box1, IncludeMutual: true, IncludeNoMutual: true);
        Transition.Exit(Box2, IncludeMutual: true, IncludeNoMutual: true);
    }

    public void Dispose()
    {
        ExitAnimations();
    }
}
