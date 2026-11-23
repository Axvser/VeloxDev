extern alias wf;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using BenchmarkDotNet.Attributes;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;
using WfControls = wf::VeloxDev.WinForms.Xaml.Visuals.Controls;
using WfPool = wf::VeloxDev.WorkflowSystem.AttachedBehaviors.ViewPool;
using WfPoolChangedEventArgs = wf::VeloxDev.WorkflowSystem.AttachedBehaviors.ViewPoolChangedEventArgs;
using WfVisuals = wf::VeloxDev.WinForms.Xaml.Visuals;

namespace VeloxDev.Adapters.Benchmarks;

// The WinForms adapter now shares the Core recycling pipeline with WPF/Avalonia, closed over the visual backend.
// These benchmarks separate the pool cost, the surface's post-change walk, and the XAML visual layer's
// layout/paint on the same 400-card tree.
[MemoryDiagnoser]
public class WinFormsViewPoolBenchmarks
{
    private const int ViewCount = 400;

    private readonly ObservableCollection<object> items = [];
    private WfVisuals.VisualHost host = null!;
    private WfControls.Canvas canvas = null!;
    private Bitmap bitmap = null!;
    private Graphics graphics = null!;
    private int changed;

    [GlobalSetup]
    public void Setup()
    {
        host = new WfVisuals.VisualHost { Size = new System.Drawing.Size(1200, 800) };
        canvas = new WfControls.Canvas();
        host.Root = canvas;
        WfPool.SetItemsSource(canvas, items);
        WfPool.SetTemplateSelector(canvas, new CardSelector());
        WfPool.Refresh(canvas);
        WfPool.Changed += OnPoolChanged;
        bitmap = new Bitmap(1200, 800);
        graphics = Graphics.FromImage(bitmap);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        WfPool.Changed -= OnPoolChanged;
        graphics.Dispose();
        bitmap.Dispose();
        WfPool.SetItemsSource(canvas, null);
        host.Dispose();
    }

    [IterationSetup]
    public void Reset()
    {
        items.Clear();
        WfPool.Refresh(canvas);
        changed = 0;
    }

    [Benchmark(Baseline = true)]
    public int AddAll()
    {
        for (var index = 0; index < ViewCount; index++)
        {
            items.Add(new NodeItem(index));
        }

        return changed;
    }

    [Benchmark]
    public void AddAllAndRender()
    {
        for (var index = 0; index < ViewCount; index++)
        {
            items.Add(new NodeItem(index));
        }

        host.RenderTo(graphics, new System.Drawing.Size(1200, 800));
    }

    private void OnPoolChanged(object? sender, WfPoolChangedEventArgs e) => changed++;

    internal static WfVisuals.VisualElement CreateCard()    {
        var root = new WfControls.Grid { Width = 230d, Height = 170d };
        root.Add(new WfControls.Border
        {
            Background = Color.FromArgb(255, 37, 37, 38),
            Margin = new wf::VeloxDev.WinForms.Xaml.Controls.Thickness(0),
        });

        var content = new WfControls.StackPanel
        {
            Margin = new wf::VeloxDev.WinForms.Xaml.Controls.Thickness(12, 36, 12, 8),
        };
        content.Add(new WfControls.TextBlock
        {
            Text = "Node title",
            Foreground = Color.FromArgb(255, 231, 231, 231),
        });
        for (var index = 0; index < 8; index++)
        {
            content.Add(new WfControls.TextBlock
            {
                Text = "runtime value / status line",
                Foreground = Color.FromArgb(255, 170, 174, 182),
                Margin = new wf::VeloxDev.WinForms.Xaml.Controls.Thickness(0, 3, 0, 0),
            });
        }

        content.Add(new WfControls.Button
        {
            Text = "Run",
            Height = 24d,
            Margin = new wf::VeloxDev.WinForms.Xaml.Controls.Thickness(0, 6, 0, 0),
        });
        content.Add(new WfControls.Button
        {
            Text = "Stop",
            Height = 24d,
            Margin = new wf::VeloxDev.WinForms.Xaml.Controls.Thickness(0, 4, 0, 0),
        });
        root.Add(content);
        return root;
    }

    internal static int CountDescendants(WfVisuals.VisualElement root)
    {
        var count = 0;
        foreach (var child in root.Children)
        {
            if (child is WfVisuals.VisualElement visual)
            {
                count += 1 + CountDescendants(visual);
            }
        }

        return count;
    }

    // Core 只物化 IWorkflowComponent；基准条目照旧带一个空命令与空通知。
    internal sealed record NodeItem(int Id) : IWorkflowComponent
    {
        private static readonly IVeloxCommand Close = new VeloxCommand((_, _) => Task.CompletedTask);

        public IVeloxCommand CloseCommand => Close;

        public event PropertyChangingEventHandler? PropertyChanging;

        public event PropertyChangedEventHandler? PropertyChanged;

        public void InitializeWorkflow()
        {
        }

        public void OnPropertyChanging(string propertyName)
            => PropertyChanging?.Invoke(this, new PropertyChangingEventArgs(propertyName));

        public void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // 基准里的条目统一是 NodeItem，选择器直接照卡模板造一个新视图。
    internal sealed class CardSelector : WfVisuals.VisualTemplateSelector
    {
        public override WfVisuals.VisualElement? SelectTemplate(object? item)
            => item is NodeItem ? CreateCard() : null;
    }
}

// WorkflowSurfaceBehavior.OnPoolChanged walks the whole visual tree after each materialization batch. This benchmark
// keeps the pool identical to AddAll and adds only that walk, so the two numbers differ by the surface hook.
[MemoryDiagnoser]
public class WinFormsViewPoolSurfaceWalkBenchmarks
{
    private const int ViewCount = 400;

    private readonly ObservableCollection<object> items = [];
    private WfVisuals.VisualHost host = null!;
    private WfControls.Canvas canvas = null!;
    private int walked;

    [GlobalSetup]
    public void Setup()
    {
        host = new WfVisuals.VisualHost { Size = new System.Drawing.Size(1200, 800) };
        canvas = new WfControls.Canvas();
        host.Root = canvas;
        WfPool.SetItemsSource(canvas, items);
        WfPool.SetTemplateSelector(canvas, new WinFormsViewPoolBenchmarks.CardSelector());
        WfPool.Refresh(canvas);
        WfPool.Changed += OnPoolChanged;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        WfPool.Changed -= OnPoolChanged;
        WfPool.SetItemsSource(canvas, null);
        host.Dispose();
    }

    [IterationSetup]
    public void Reset()
    {
        items.Clear();
        WfPool.Refresh(canvas);
        walked = 0;
    }

    [Benchmark]
    public int AddAllWithSurfaceWalk()
    {
        for (var index = 0; index < ViewCount; index++)
        {
            items.Add(new WinFormsViewPoolBenchmarks.NodeItem(index));
        }

        return walked;
    }

    private void OnPoolChanged(object? sender, WfPoolChangedEventArgs e)
        => walked += WinFormsViewPoolBenchmarks.CountDescendants(canvas);
}

// A viewport sweep: half the visible set leaves and half enters, the shape of panning across the graph.
[MemoryDiagnoser]
public class WinFormsViewPoolChurnBenchmarks
{
    private const int ViewCount = 400;

    private readonly ObservableCollection<object> items = [];
    private WfVisuals.VisualHost host = null!;
    private WfControls.Canvas canvas = null!;
    private Bitmap bitmap = null!;
    private Graphics graphics = null!;
    private int generation;

    [GlobalSetup]
    public void Setup()
    {
        host = new WfVisuals.VisualHost { Size = new System.Drawing.Size(1200, 800) };
        canvas = new WfControls.Canvas();
        host.Root = canvas;
        WfPool.SetItemsSource(canvas, items);
        WfPool.SetTemplateSelector(canvas, new WinFormsViewPoolBenchmarks.CardSelector());
        WfPool.Refresh(canvas);
        bitmap = new Bitmap(1200, 800);
        graphics = Graphics.FromImage(bitmap);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        graphics.Dispose();
        bitmap.Dispose();
        WfPool.SetItemsSource(canvas, null);
        host.Dispose();
    }

    [IterationSetup]
    public void Reset()
    {
        items.Clear();
        WfPool.Refresh(canvas);
        for (var index = 0; index < ViewCount; index++)
        {
            items.Add(new WinFormsViewPoolBenchmarks.NodeItem(index));
        }
    }

    [Benchmark(Baseline = true)]
    public void ChurnAndRender()
    {
        generation++;
        for (var index = 0; index < ViewCount / 2; index++)
        {
            items.RemoveAt(0);
        }

        for (var index = 0; index < ViewCount / 2; index++)
        {
            items.Add(new WinFormsViewPoolBenchmarks.NodeItem(generation * ViewCount + index));
        }

        host.RenderTo(graphics, new System.Drawing.Size(1200, 800));
    }
}

// The same card tree with no pool: create the views directly on the Canvas, then measure add+layout+paint.
[MemoryDiagnoser]
public class WinFormsVisualLayerBenchmarks
{
    private const int ViewCount = 400;

    private WfVisuals.VisualHost host = null!;
    private WfControls.Canvas canvas = null!;
    private Bitmap bitmap = null!;
    private Graphics graphics = null!;
    private WfVisuals.VisualElement[] cards = null!;

    [GlobalSetup]
    public void Setup()
    {
        host = new WfVisuals.VisualHost { Size = new System.Drawing.Size(1200, 800) };
        canvas = new WfControls.Canvas();
        host.Root = canvas;
        bitmap = new Bitmap(1200, 800);
        graphics = Graphics.FromImage(bitmap);
        cards = new WfVisuals.VisualElement[ViewCount];
        for (var index = 0; index < ViewCount; index++)
        {
            cards[index] = WinFormsViewPoolBenchmarks.CreateCard();
        }

        host.RenderTo(graphics, new System.Drawing.Size(1200, 800));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        graphics.Dispose();
        bitmap.Dispose();
        host.Dispose();
    }

    [IterationSetup]
    public void Reset() => canvas.Children.Clear();

    [Benchmark(Baseline = true)]
    public void AddAndRender()
    {
        foreach (var card in cards)
        {
            canvas.Add(card);
        }

        host.RenderTo(graphics, new System.Drawing.Size(1200, 800));
    }

    [Benchmark]
    public void ChurnAndRender()
    {
        foreach (var card in cards)
        {
            canvas.Add(card);
        }

        for (var index = 0; index < ViewCount / 2; index++)
        {
            canvas.Children.RemoveAt(0);
        }

        for (var index = 0; index < ViewCount / 2; index++)
        {
            canvas.Add(WinFormsViewPoolBenchmarks.CreateCard());
        }

        host.RenderTo(graphics, new System.Drawing.Size(1200, 800));
    }
}

// Paint-only: the tree is built once and the benchmark measures one steady-state RenderTo.
[MemoryDiagnoser]
public class WinFormsVisualRenderBenchmarks
{
    private const int ViewCount = 400;

    private WfVisuals.VisualHost host = null!;
    private Bitmap bitmap = null!;
    private Graphics graphics = null!;

    [GlobalSetup]
    public void Setup()
    {
        host = new WfVisuals.VisualHost { Size = new System.Drawing.Size(1200, 800) };
        var canvas = new WfControls.Canvas();
        host.Root = canvas;
        for (var index = 0; index < ViewCount; index++)
        {
            canvas.Add(WinFormsViewPoolBenchmarks.CreateCard());
        }

        bitmap = new Bitmap(1200, 800);
        graphics = Graphics.FromImage(bitmap);
        host.RenderTo(graphics, new System.Drawing.Size(1200, 800));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        graphics.Dispose();
        bitmap.Dispose();
        host.Dispose();
    }

    [Benchmark]
    public void Render()
        => host.RenderTo(graphics, new System.Drawing.Size(1200, 800));
}
