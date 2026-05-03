using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.MVVM;

namespace Demo.ViewModels;

/* 不需要继承任何类，也不需要显示声明接口 */
/* 提示：你可以继承其它类，但是请不要与 MVVM 相关，因为此工具集已经生成了完整的 MVVM 支持，需要避免与其它工具产生冲突 */
public partial class MainWindowViewModel : ObservableViewModelBase
{
    public MainWindowViewModel()
    {
        Items =
        [
            "Item1",
            "Item2",
            "Item3"
        ];

        SelectedItem = Items.FirstOrDefault();
    }

    /* 快速生成你的属性 */
    [VeloxProperty] private int _index = 0;
    [VeloxProperty] private string _greeting = $"current index: 0";
    [VeloxProperty] private ObservableCollection<string> _items = [];
    [VeloxProperty] private string? _selectedItem;
    [VeloxProperty] private string _selectedItemSummary = "当前选中: (无)";
    [VeloxProperty] private string _collectionStatus = "等待集合通知";
    [VeloxProperty] private string _collectionTrace = "OnCollectionChanged<T> 尚未触发";

    /* 属性回调 */
    partial void OnIndexChanged(int oldValue, int newValue)
    {
        MinusCommand.Notify(); // 通知 MinusCommand 的可执行性需要更新
    }

    partial void OnSelectedItemChanged(string? oldValue, string? newValue)
    {
        SelectedItemSummary = newValue is null ? "当前选中: (无)" : $"当前选中: {newValue}";
        RemoveSelectedItemCommand.Notify();
    }

    partial void OnItemsChanged(ObservableCollection<string> oldValue, ObservableCollection<string> newValue)
    {
        if (SelectedItem is not null && !newValue.Contains(SelectedItem))
        {
            SelectedItem = newValue.FirstOrDefault();
        }

        RefreshCollectionCommands();
    }

    protected override void OnCollectionChanged<T>(string propertyName, NotifyCollectionChangedEventArgs e, IEnumerable<T>? oldItems, IEnumerable<T>? newItems)
    {
        CollectionTrace = $"{propertyName}: {e.Action} | old=[{FormatItems(oldItems)}] | new=[{FormatItems(newItems)}]";
    }

    /* 一个默认的 Command，名字自动截取，无可用性验证，排队执行 */
    [VeloxCommand(name: "Auto", canValidate: false, semaphore: 1)]
    private Task Plus(object? sender, CancellationToken ct)
    {
        Index++;
        Greeting = $"current index: {Index}";
        return Task.CompletedTask;
    }

    /* 开启可用性验证 */
    [VeloxCommand(canValidate: true)]
    private Task Minus(object? sender, CancellationToken ct)
    {
        Index--;
        Greeting = $"current index: {Index}";
        return Task.CompletedTask;
    }
    /* 此时必须实现此分部方法 */
    private partial bool CanExecuteMinusCommand(object? parameter)
    {
        return _index > 0;
    }

    [VeloxCommand]
    private Task AddItem(object? sender, CancellationToken ct)
    {
        Index++;
        var item = $"ConditionalSlot {Index:00}";
        Items.Add(item);
        SelectedItem = item;
        Greeting = $"current index: {Index}";
        return Task.CompletedTask;
    }

    [VeloxCommand(canValidate: true)]
    private Task RemoveSelectedItem(object? sender, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(SelectedItem))
        {
            return Task.CompletedTask;
        }

        var target = SelectedItem;
        Items.Remove(target);
        if (Items.Count > 0)
        {
            SelectedItem = Items[0];
        }

        Greeting = $"current index: {Index}";
        return Task.CompletedTask;
    }

    private partial bool CanExecuteRemoveSelectedItemCommand(object? parameter)
    {
        return !string.IsNullOrWhiteSpace(_selectedItem) && _items.Contains(_selectedItem);
    }

    [VeloxCommand(canValidate: true)]
    private Task MoveLastToFirst(object? sender, CancellationToken ct)
    {
        if (Items.Count <= 1)
        {
            return Task.CompletedTask;
        }

        Items.Move(Items.Count - 1, 0);
        SelectedItem = Items[0];
        return Task.CompletedTask;
    }

    private partial bool CanExecuteMoveLastToFirstCommand(object? parameter)
    {
        return _items.Count > 1;
    }

    [VeloxCommand]
    private Task ReplaceItems(object? sender, CancellationToken ct)
    {
        Items =
        [
            "Item1",
            "Item2",
            "Item3"
        ];

        SelectedItem = Items.FirstOrDefault();
        return Task.CompletedTask;
    }

    [VeloxCommand]
    private Task ClearItems(object? sender, CancellationToken ct)
    {
        Items.Clear();
        return Task.CompletedTask;
    }

    /* 无阻中断 */
    private void FreeCommand()
    {
        MinusCommand.Lock();   // 进入锁定状态，阻止新的命令触发但不会中断当前执行中的命令

        MinusCommand.Interrupt();    // 中断当前命令
        MinusCommand.Clear();        // 中断当前命令和正在排队的所有命令

        MinusCommand.UnLock(); // 解除锁定
    }

    /* 可等待中断 */
    private async Task FreeCommandAsync()
    {
        MinusCommand.Lock();   // 进入锁定状态，阻止新的命令触发但不会中断当前执行中的命令

        await MinusCommand.InterruptAsync();    // 中断当前命令
        await MinusCommand.ClearAsync(); // 中断当前命令和正在排队的所有命令

        MinusCommand.UnLock(); // 解除锁定
    }

    partial void OnItemAddedToItems(IEnumerable<string> items)
    {
        var materialized = items.ToArray();
        CollectionStatus = $"新增 {materialized.Length} 项: {FormatItems(materialized)} | 当前总数: {Items.Count}";
        RefreshCollectionCommands();
    }

    partial void OnItemRemovedFromItems(IEnumerable<string> items)
    {
        var materialized = items.ToArray();
        if (SelectedItem is not null && !Items.Contains(SelectedItem))
        {
            SelectedItem = Items.FirstOrDefault();
        }

        CollectionStatus = $"移除 {materialized.Length} 项: {FormatItems(materialized)} | 当前总数: {Items.Count}";
        RefreshCollectionCommands();
    }

    partial void OnItemMovedInItems(IEnumerable<string> items)
    {
        var materialized = items.ToArray();
        CollectionStatus = $"移动项: {FormatItems(materialized)} | 当前总数: {Items.Count}";
        RefreshCollectionCommands();
    }

    partial void OnItemsResetInItems()
    {
        SelectedItem = null;
        CollectionStatus = "集合已重置";
        RefreshCollectionCommands();
    }

    private void RefreshCollectionCommands()
    {
        RemoveSelectedItemCommand.Notify();
        MoveLastToFirstCommand.Notify();
    }

    private static string FormatItems<T>(IEnumerable<T>? items)
    {
        if (items is null)
        {
            return "(null)";
        }

        var materialized = items.Select(item => item?.ToString() ?? "(null)").ToArray();
        return materialized.Length == 0 ? "(empty)" : string.Join(", ", materialized);
    }
}