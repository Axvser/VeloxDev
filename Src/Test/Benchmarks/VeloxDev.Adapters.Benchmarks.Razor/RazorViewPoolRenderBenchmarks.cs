using System.Diagnostics;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.Adapters.Benchmarks.Razor;

/// <summary>
/// Measures the Blazor renderer work the Razor adapter's <see cref="ViewPool"/> drives: one render tree for the
/// spatial visible set of the synthetic workflow, built by the framework's <see cref="HtmlRenderer"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is a headless renderer benchmark, not a headed window benchmark. Razor's <c>ViewPool</c> is renderer-native
/// (see <c>memory/modules/WorkflowSystem/view-hosting.md</c> §七), so there is no imperative view system to drive.
/// What runs here is the production component and its keyed diff, plus the framework renderer's own tree work.
/// </para>
/// <para>
/// The template is a representative node/link card, deliberately lighter than the demo's full
/// <c>TemplateNodeView</c>/<c>TemplateLinkView</c>. The numbers are therefore a lower bound for the adapter layer and
/// are not comparable to the headed pipeline figures.
/// </para>
/// </remarks>
[Config(typeof(RazorBenchmarkConfig))]
public class RazorViewPoolRenderBenchmarks
{
    private RenderScenario _scenario = null!;
    private ParameterView _parameters;
    private HtmlRenderer? _initialRenderer;
    private HtmlRenderer _scrollRenderer = null!;
    private Action? _renderInitial;
    private Action? _applyViewport;
    private Viewport _nextViewport;

    /// <summary>Builds the shared synthetic workflow and renders the reusable scroll pool once, outside measurement.</summary>
    [GlobalSetup]
    public void GlobalSetup()
    {
        WarnIfDependenciesAreUnoptimized();

        _scenario = RenderScenario.Create();
        _parameters = _scenario.CreateParameters();
        _applyViewport = ApplyViewport;

        _scrollRenderer = RenderScenario.CreateRenderer();
        _scrollRenderer.Dispatcher
            .InvokeAsync(() => _scrollRenderer.BeginRenderingComponent<ViewPool>(_parameters))
            .GetAwaiter()
            .GetResult();
    }

    /// <summary>Resets the visible set to the origin viewport and gives the initial-render case its own renderer.</summary>
    [IterationSetup(Target = nameof(RenderInitialVisible))]
    public void ResetForInitialRender()
    {
        ResetViewport();
        _initialRenderer = RenderScenario.CreateRenderer();
        _renderInitial = () => _initialRenderer.BeginRenderingComponent<ViewPool>(_parameters);
    }

    /// <summary>Disposes the renderer of the just-finished initial render.</summary>
    [IterationCleanup(Target = nameof(RenderInitialVisible))]
    public void DisposeInitialRenderer()
    {
        _initialRenderer?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _initialRenderer = null;
        _renderInitial = null;
    }

    /// <summary>Puts the reused pool back at the origin viewport before each sweep.</summary>
    [IterationSetup(Target = nameof(RenderScrollSweep))]
    public void ResetForScrollSweep() => ResetViewport();

    /// <summary>Renders the pool once from scratch: the visible set's initial render tree.</summary>
    [Benchmark(Baseline = true)]
    public void RenderInitialVisible()
    {
        _initialRenderer!.Dispatcher.InvokeAsync(_renderInitial!).GetAwaiter().GetResult();
    }

    /// <summary>Moves the viewport through the diagonal sweep; one invocation is one 20-step sweep.</summary>
    [Benchmark(OperationsPerInvoke = RenderScenario.ScrollSteps)]
    public void RenderScrollSweep()
    {
        for (var index = 0; index < RenderScenario.ScrollSteps; index++)
        {
            _nextViewport = _scenario.ScrollViewports[index];
            _scrollRenderer.Dispatcher.InvokeAsync(_applyViewport!).GetAwaiter().GetResult();
        }
    }

    /// <summary>Disposes the reusable renderer and detaches the tree from the shared tick channel.</summary>
    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _scrollRenderer.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _scenario.Tree.GetHelper().Uninstall(_scenario.Tree);
    }

    // 复位不在计时区内；它顺带让复用的 renderer 重渲染一次，把下一个测量起点摆正。
    private void ResetViewport()
        => _scrollRenderer.Dispatcher
            .InvokeAsync(() => _scenario.Helper.Viewport = _scenario.Origin)
            .GetAwaiter()
            .GetResult();

    private void ApplyViewport() => _scenario.Helper.Viewport = _nextViewport;

    // 校验器已在 config 里保持开启，未优化依赖会在运行前被拒绝；这里保留一条显式提示，
    // 防止有人绕过 BDN 直接调用基准方法。
    private static void WarnIfDependenciesAreUnoptimized()
    {
        if (!IsUnoptimized(typeof(ViewPool)) && !IsUnoptimized(typeof(TreeDefaultViewModel)))
        {
            return;
        }

        Console.WriteLine(
            "warning: this run references Debug-built VeloxDev.Razor/VeloxDev.Core. "
            + "Build the graph with -p:Optimize=true for representative times; allocations are less affected.");
    }

    private static bool IsUnoptimized(Type type)
        => type.Assembly.GetCustomAttribute<DebuggableAttribute>()?.IsJITOptimizerDisabled == true;
}
