extern alias wf;

using System.Drawing;
using System.Drawing.Drawing2D;
using BenchmarkDotNet.Attributes;
using WfControls = Easel.Visuals.Controls;
using WfVisuals = Easel.Visuals;

namespace VeloxDev.Adapters.Benchmarks;

// A drag frame on the visual layer alone: the cards are prebuilt, so the measured cost is the layout and paint
// that a node move or a camera pan actually performs. The view pool is not involved.
[MemoryDiagnoser]
public class WinFormsVisualFrameBenchmarks
{
    [Params(15, 50)]
    public int ViewCount { get; set; }

    private WfVisuals.VisualHost host = null!;
    private WfControls.Canvas canvas = null!;
    private Bitmap bitmap = null!;
    private Graphics graphics = null!;
    private WfVisuals.VisualElement[] cards = null!;
    private double move;

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
            var card = WinFormsViewPoolBenchmarks.CreateCard();
            WfControls.Canvas.SetLeft(card, 20d + (index % 5) * 235d);
            WfControls.Canvas.SetTop(card, 20d + (index / 5) * 150d);
            cards[index] = card;
            canvas.Add(card);
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

    [Benchmark(Baseline = true)]
    public void RenderStatic()
        => host.RenderTo(graphics, new System.Drawing.Size(1200, 800));

    [Benchmark]
    public void MoveNodeAndRender()
    {
        move += 2d;
        WfControls.Canvas.SetLeft(cards[0], 20d + (move % 400d));
        host.RenderTo(graphics, new System.Drawing.Size(1200, 800));
    }

    [Benchmark]
    public void PanAndRender()
    {
        move += 2d;
        canvas.Transform = new Matrix(1f, 0f, 0f, 1f, (float)(move % 400d), 0f);
        canvas.InvalidateVisual();
        host.RenderTo(graphics, new System.Drawing.Size(1200, 800));
    }
}
