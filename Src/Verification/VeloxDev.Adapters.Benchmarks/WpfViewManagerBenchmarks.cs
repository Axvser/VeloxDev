extern alias wpf;

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BenchmarkDotNet.Attributes;
using WpfViewManager = wpf::VeloxDev.WorkflowSystem.AttachedBehaviors.ViewManager;

namespace VeloxDev.Adapters.Benchmarks;

// W6/W7：ViewManager 挂载 N 项后逐项移除，复现 RemoveAt(0)、Any/FirstOrDefault/RemoveAll 的线性扫描。
[MemoryDiagnoser]
public class WpfViewManagerBenchmarks
{
	public sealed class Item;

	[Params(500, 2000)]
	public int N;

	private Dispatcher _ui = null!;
	private Thread _thread = null!;
	private Canvas _panel = null!;
	private Item[] _items = null!;

	[GlobalSetup]
	public void Setup()
	{
		// 进程内工具链不在 STA 上运行，单独起一个 STA 线程承载 WPF Dispatcher。
		using var ready = new ManualResetEventSlim();
		_thread = new Thread(() =>
		{
			_ui = Dispatcher.CurrentDispatcher;
			ready.Set();
			Dispatcher.Run();
		});
		_thread.SetApartmentState(ApartmentState.STA);
		_thread.IsBackground = true;
		_thread.Start();
		ready.Wait();

		_ui.Invoke(() =>
		{
			_panel = new Canvas();
			var template = new DataTemplate(typeof(Item)) { VisualTree = new FrameworkElementFactory(typeof(Border)) };
			_panel.Resources.Add(new DataTemplateKey(typeof(Item)), template);
		});
		_items = new Item[N];
		for (var i = 0; i < N; i++) _items[i] = new Item();
	}

	[GlobalCleanup]
	public void Cleanup() => _ui.InvokeShutdown();

	[Benchmark]
	public int AttachDrainRemoveAll()
	{
		ObservableCollection<Item>? collection = null;
		WpfViewManager? manager = null;
		_ui.Invoke(() =>
		{
			collection = new ObservableCollection<Item>(_items);
			manager = new WpfViewManager(_panel);
			manager.Attach(collection);
		});
		// ContextIdle 低于 Background：分批渲染全部跑完后才会执行到这里。
		_ui.Invoke(static () => { }, DispatcherPriority.ContextIdle);
		return _ui.Invoke(() =>
		{
			// 从头部逐个移除：每次 Remove 都触发一次活动视图查找。
			while (collection!.Count > 0) collection.RemoveAt(0);
			manager!.Detach();
			var count = _panel.Children.Count;
			_panel.Children.Clear();
			return count;
		});
	}
}
