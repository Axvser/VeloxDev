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

    protected override void OnInitialized()
    {
        // 在 Blazor Server circuit 线程上捕获 SynchronizationContext
        // 供 VeloxDev UIThreadInspector 使用（每个组件实例独立初始化）
        UIThreadInspector.CaptureUIThread();

        // 订阅属性变更，驱动 Blazor 重渲染
        Box0.PropertyChanged += (_, _) => InvokeAsync(StateHasChanged);
        Box1.PropertyChanged += (_, _) => InvokeAsync(StateHasChanged);
        Box2.PropertyChanged += (_, _) => InvokeAsync(StateHasChanged);

        // 记录 Box0 的初始状态快照，用于 Reset
        // VeloxDev 动画的核心概念是 "一切皆状态"
        _snapshot0 = Box0.Snapshot(
            b => b.X,
            b => b.Y,
            b => b.Color,
            b => b.Opacity,
            b => b.Width,
            b => b.Height,
            b => b.Scale,
            b => b.Rotate);
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

    private void ResetBox0()
    {
        // 以零时长过渡立即恢复到快照记录的初始状态
        _snapshot0.Effect(TransitionEffects.Empty).Execute(Box0);
    }

    private void ExitAnimations()
    {
        // IncludeMutual   表示是否终结 CanMutualTask: true 的动画
        // IncludeNoMutual 表示是否终结 CanMutualTask: false 的动画
        Transition.Exit(Box0, IncludeMutual: true, IncludeNoMutual: true);
        Transition.Exit(Box1);
        Transition.Exit(Box2);
    }

    public void Dispose()
    {
        ExitAnimations();
    }
}
