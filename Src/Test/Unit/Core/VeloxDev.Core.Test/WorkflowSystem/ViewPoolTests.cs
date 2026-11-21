using System.Collections.ObjectModel;
using System.ComponentModel;
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.Core.Test.WorkflowSystem;

[TestClass]
public sealed class ViewPoolTests
{
    [TestMethod]
    public void MaterializesInBatches()
    {
        var source = new ObservableCollection<IWorkflowComponent>
        {
            new FakeItemA(),
            new FakeItemA(),
            new FakeItemA(),
        };
        using var scene = new FakeScene(source, batchSize: 2);

        Assert.AreEqual(0, scene.Pool.ActiveViewCount);
        Assert.AreEqual(3, scene.Pool.PendingViewCount);

        scene.Scheduler.RunOne();
        Assert.AreEqual(2, scene.Pool.ActiveViewCount);
        Assert.AreEqual(1, scene.Pool.PendingViewCount);

        scene.Scheduler.RunAll();
        Assert.AreEqual(3, scene.Pool.ActiveViewCount);
        Assert.AreEqual(0, scene.Pool.PendingViewCount);
        Assert.AreEqual(3, scene.Factory.CreatedViews.Count);
    }

    [TestMethod]
    public void RemovedViewIsReusedForTheNextItemOfTheSameType()
    {
        var first = new FakeItemA();
        var source = new ObservableCollection<IWorkflowComponent> { first };
        using var scene = new FakeScene(source);
        scene.Scheduler.RunAll();

        var view = scene.Factory.CreatedViews[0];
        source.Remove(first);

        Assert.AreEqual(0, scene.Pool.ActiveViewCount);
        Assert.AreEqual(1, scene.Pool.RecycledViewCount);

        var second = new FakeItemA();
        source.Add(second);
        scene.Scheduler.RunAll();

        Assert.AreEqual(1, scene.Pool.ActiveViewCount);
        Assert.AreEqual(1, scene.Factory.CreatedViews.Count);
        Assert.AreSame(second, view.Context);
        Assert.IsTrue(view.Visible);
    }

    [TestMethod]
    public void ResetRecyclesEverythingAndRebuildsFromTheCurrentItems()
    {
        var source = new ObservableCollection<IWorkflowComponent>
        {
            new FakeItemA(),
            new FakeItemB(),
        };
        using var scene = new FakeScene(source);
        scene.Scheduler.RunAll();

        source.Clear();

        Assert.AreEqual(0, scene.Pool.ActiveViewCount);
        Assert.AreEqual(2, scene.Pool.RecycledViewCount);

        source.Add(new FakeItemA());
        scene.Scheduler.RunAll();

        Assert.AreEqual(1, scene.Pool.ActiveViewCount);
        Assert.AreEqual(2, scene.Factory.CreatedViews.Count);
    }

    [TestMethod]
    public void MissingTemplateSkipsTheTypeAndDoesNotRetry()
    {
        var source = new ObservableCollection<IWorkflowComponent>
        {
            new FakeItemB(),
            new FakeItemB(),
        };
        using var scene = new FakeScene(source);
        scene.Factory.ShouldCreate = item => item is not FakeItemB;

        scene.Scheduler.RunAll();

        Assert.AreEqual(0, scene.Pool.ActiveViewCount);
        Assert.AreEqual(0, scene.Pool.PendingViewCount);
        Assert.AreEqual(1, scene.Factory.CreateAttemptsFor(typeof(FakeItemB)));
    }

    [TestMethod]
    public void OneFailingItemDoesNotStopTheRestOfTheBatch()
    {
        var source = new ObservableCollection<IWorkflowComponent>
        {
            new FakeItemA(),
            new FakeItemB(),
        };
        using var scene = new FakeScene(source);
        scene.Factory.ThrowFor = typeof(FakeItemA);

        scene.Scheduler.RunAll();

        Assert.AreEqual(1, scene.Pool.ActiveViewCount);
        Assert.AreEqual(1, scene.Diagnostics.Failures.Count);
        Assert.AreEqual(typeof(FakeItemA), scene.Diagnostics.Failures[0].ItemType);
        Assert.AreEqual(ViewStage.Create, scene.Diagnostics.Failures[0].Stage);
    }

    [TestMethod]
    public void DisposeReleasesActiveViewsAndStopsTracking()
    {
        var source = new ObservableCollection<IWorkflowComponent> { new FakeItemA() };
        var scene = new FakeScene(source);
        scene.Scheduler.RunAll();

        var view = scene.Factory.CreatedViews[0];
        scene.System.Dispose();

        Assert.IsFalse(view.Visible);
        Assert.AreEqual(1, scene.Factory.CreatedViews.Count);

        source.Add(new FakeItemB());
        scene.Scheduler.RunAll();

        Assert.AreEqual(1, scene.Factory.CreatedViews.Count);
    }

    [TestMethod]
    public void RefusedScheduleRetriesOnTheNextVisibleSetChange()
    {
        var source = new ObservableCollection<IWorkflowComponent> { new FakeItemA() };
        using var scene = new FakeScene(source, scheduleAccepted: false);

        Assert.AreEqual(1, scene.Pool.PendingViewCount);
        Assert.AreEqual(1, scene.Scheduler.ScheduleAttempts);
        Assert.AreEqual(1, scene.Diagnostics.Failures.Count);
        Assert.AreEqual(ViewStage.Schedule, scene.Diagnostics.Failures[0].Stage);

        scene.Scheduler.ScheduleAccepted = true;
        source.Add(new FakeItemB());
        scene.Scheduler.RunAll();

        Assert.AreEqual(2, scene.Pool.ActiveViewCount);
        Assert.AreEqual(2, scene.Scheduler.ScheduleAttempts);
    }

    [TestMethod]
    public void LinkLayerOwnsItsItemsAndTheMainPoolSkipsThem()
    {
        var source = new ObservableCollection<IWorkflowComponent>
        {
            new FakeItemA(),
            new FakeItemB(),
        };
        using var scene = new FakeScene(source, handlesLinks: item => item is FakeItemB);
        scene.LinkLayer.IsActive = true;

        scene.Scheduler.RunAll();

        Assert.AreEqual(1, scene.Pool.ActiveViewCount);
        Assert.AreEqual(1, scene.Factory.CreatedViews.Count);
        Assert.IsTrue(scene.LinkLayer.Renders.Any(
            rendered => rendered.Any(item => item is FakeItemB)));
    }

    [TestMethod]
    public void LayerPolicyAndMeasureHostRunAfterArrangement()
    {
        var source = new ObservableCollection<IWorkflowComponent> { new FakeItemA() };
        using var scene = new FakeScene(source, zOrder: 7);

        scene.Scheduler.RunAll();

        Assert.AreEqual(7, scene.Container.LastZOrder);
        Assert.AreEqual(1, scene.MeasureHost.MeasureCount);
    }

    [TestMethod]
    public void RecycledViewsAreCappedByThePoolPolicy()
    {
        var first = new FakeItemA();
        var second = new FakeItemA();
        var source = new ObservableCollection<IWorkflowComponent> { first, second };
        using var scene = new FakeScene(source, batchSize: 4, maxRecycledViewsPerType: 1);
        scene.Scheduler.RunAll();

        source.Remove(first);
        source.Remove(second);

        Assert.AreEqual(1, scene.Pool.RecycledViewCount);
        Assert.AreEqual(1, scene.Container.DestroyCount);
    }

    private sealed class FakeView
    {
        public IWorkflowComponent? Context { get; set; }
        public bool Visible { get; set; }
        public int ArrangeCount { get; set; }
    }

    private class FakeItemBase : IWorkflowComponent
    {
        public IVeloxCommand CloseCommand { get; } = new StubCommand();

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

    private sealed class FakeItemA : FakeItemBase
    {
    }

    private sealed class FakeItemB : FakeItemBase
    {
    }

    private sealed class Failure
    {
        public Failure(Type itemType, ViewStage stage, Exception? exception)
        {
            ItemType = itemType;
            Stage = stage;
            Exception = exception;
        }

        public Type ItemType { get; }

        public ViewStage Stage { get; }

        public Exception? Exception { get; }
    }

    private sealed class FakeFactory : IViewFactory<IWorkflowComponent, FakeView>
    {
        private readonly Dictionary<Type, int> _createAttempts = [];

        public Func<IWorkflowComponent, bool> ShouldCreate { get; set; } = _ => true;
        public Type? ThrowFor { get; set; }
        public List<FakeView> CreatedViews { get; } = [];

        public int CreateAttemptsFor(Type type)
            => _createAttempts.TryGetValue(type, out var count) ? count : 0;

        public bool WantsItem(IWorkflowComponent item) => true;

        public FakeView? CreateView(IWorkflowComponent item)
        {
            _createAttempts[item.GetType()] = CreateAttemptsFor(item.GetType()) + 1;
            if (ThrowFor == item.GetType())
            {
                throw new InvalidOperationException("template failed");
            }

            if (!ShouldCreate(item))
            {
                return null;
            }

            var view = new FakeView();
            CreatedViews.Add(view);
            return view;
        }
    }

    private sealed class FakeContainer : IViewContainer<object, FakeView, IWorkflowComponent>
    {
        public int LastZOrder { get; private set; }

        public int DestroyCount { get; private set; }

        public void Attach(object host, FakeView view)
        {
        }

        public void Detach(object host, FakeView view)
        {
        }

        public void Activate(FakeView view, IWorkflowComponent item)
        {
            view.Context = item;
            view.Visible = true;
        }

        public void Deactivate(FakeView view)
        {
            view.Context = null;
            view.Visible = false;
        }

        public void SetZOrder(FakeView view, int zIndex) => LastZOrder = zIndex;

        public void Destroy(object host, FakeView view) => DestroyCount++;
    }

    private sealed class FakePoolPolicy : IViewPoolPolicy
    {
        public FakePoolPolicy(
            int batchSize = 3,
            int maxRecycledViewsPerType = 0,
            double frameBudgetMilliseconds = 0d)
        {
            BatchSize = batchSize;
            MaxRecycledViewsPerType = maxRecycledViewsPerType;
            FrameBudgetMilliseconds = frameBudgetMilliseconds;
        }

        public int BatchSize { get; }

        public int MaxRecycledViewsPerType { get; }

        public double FrameBudgetMilliseconds { get; }
    }

    private sealed class FakeLayerPolicy : IViewLayerPolicy<IWorkflowComponent>
    {
        private readonly int _zOrder;

        public FakeLayerPolicy(int zOrder = 0) => _zOrder = zOrder;

        public int GetZOrder(IWorkflowComponent item) => _zOrder;
    }

    private sealed class FakeLinkLayer : IViewLinkLayer<object, IWorkflowComponent>
    {
        public Func<IWorkflowComponent, bool> HandledPredicate { get; set; } = _ => false;

        public List<IReadOnlyList<IWorkflowComponent>> Renders { get; } = [];

        public bool IsActive { get; set; }

        public bool Handles(IWorkflowComponent item) => HandledPredicate(item);

        public void Render(object host, IReadOnlyList<IWorkflowComponent> items) => Renders.Add(items);
    }

    private sealed class FakeMeasureHost : IViewMeasureHost<object, FakeView, IWorkflowComponent>
    {
        public int MeasureCount { get; private set; }

        public void Measure(object host, FakeView view, IWorkflowComponent item) => MeasureCount++;
    }

    private sealed class FakeArranger : IViewArranger<IWorkflowComponent, FakeView>
    {
        public void Arrange(FakeView view, IWorkflowComponent item)
            => view.ArrangeCount++;
    }

    private sealed class FakeScheduler : IViewScheduler<int>
    {
        private readonly List<Action> _batches = [];

        public int BatchPriority { get; set; }
        public bool ScheduleAccepted { get; set; } = true;
        public int ScheduleAttempts { get; private set; }

        public bool Schedule(Action batch, int priority)
        {
            ScheduleAttempts++;
            if (!ScheduleAccepted)
            {
                return false;
            }

            _batches.Add(batch);
            return true;
        }

        public void RunOne()
        {
            var batch = _batches[0];
            _batches.RemoveAt(0);
            batch();
        }

        public void RunAll()
        {
            while (_batches.Count > 0)
            {
                RunOne();
            }
        }
    }

    private sealed class FakeDiagnostics : IViewDiagnostics<IWorkflowComponent>
    {
        public List<Failure> Failures { get; } = [];

        public void Failed(IWorkflowComponent? item, ViewStage stage, Exception? exception)
            => Failures.Add(new Failure(item?.GetType() ?? typeof(object), stage, exception));
    }

    private sealed class FakeScene : IDisposable
    {
        public FakeScene(
            ObservableCollection<IWorkflowComponent> source,
            int batchSize = 2,
            bool scheduleAccepted = true,
            int maxRecycledViewsPerType = 0,
            Func<IWorkflowComponent, bool>? handlesLinks = null,
            int zOrder = 0)
        {
            Factory = new FakeFactory();
            Container = new FakeContainer();
            Arranger = new FakeArranger();
            Scheduler = new FakeScheduler { ScheduleAccepted = scheduleAccepted };
            Diagnostics = new FakeDiagnostics();
            PoolPolicy = new FakePoolPolicy(batchSize, maxRecycledViewsPerType);
            LayerPolicy = new FakeLayerPolicy(zOrder);
            LinkLayer = new FakeLinkLayer
            {
                HandledPredicate = handlesLinks ?? (_ => false),
                IsActive = handlesLinks is not null,
            };
            MeasureHost = new FakeMeasureHost();
            System = new ViewSourcePipelineCore<
                object,
                FakeView,
                IWorkflowComponent,
                int,
                FakeFactory,
                FakeContainer,
                FakeArranger,
                FakeScheduler,
                FakeDiagnostics>(
                new object(),
                Factory,
                Container,
                Arranger,
                Scheduler,
                Diagnostics,
                source,
                PoolPolicy,
                LayerPolicy,
                LinkLayer,
                MeasureHost);
        }

        public FakeFactory Factory { get; }

        public FakeContainer Container { get; }

        public FakeArranger Arranger { get; }

        public FakeScheduler Scheduler { get; }

        public FakeDiagnostics Diagnostics { get; }

        public FakePoolPolicy PoolPolicy { get; }

        public FakeLayerPolicy LayerPolicy { get; }

        public FakeLinkLayer LinkLayer { get; }

        public FakeMeasureHost MeasureHost { get; }

        public ViewSourcePipelineCore<
            object,
            FakeView,
            IWorkflowComponent,
            int,
            FakeFactory,
            FakeContainer,
            FakeArranger,
            FakeScheduler,
            FakeDiagnostics> System { get; }

        public ViewPool<object, FakeView, IWorkflowComponent, int> Pool => System.Pool;

        public void Dispose() => System.Dispose();
    }
}
