using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Demo.Commands;
using VeloxDev.MVVM;

namespace Demo.ViewModels;

// 不必继承任何类，也不必显式声明接口。
// 可以继承其他类，但避开 MVVM 相关的基类：本工具包已提供完整 MVVM 支持，再继承别的 MVVM 基类可能冲突。
public partial class MainWindowViewModel : ObservableViewModelBase
{
    /// <summary>Every <c>[VeloxCommand]</c> signature, one command each, with its validator. Shared so both demos show the same set.</summary>
    public CommandGalleryViewModel Gallery { get; } = new();

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

    // 快速生成属性
    [VeloxProperty] private int _index = 0;
    [VeloxProperty] private string _greeting = $"current index: 0";
    [VeloxProperty] private ObservableCollection<string> _items = [];
    [VeloxProperty] private string? _selectedItem;
    [VeloxProperty] private string _selectedItemSummary = "当前选中: (无)";
    [VeloxProperty] private string _collectionStatus = "等待集合通知";
    [VeloxProperty] private string _collectionTrace = "OnCollectionChanged<T> 尚未触发";

    // 属性回调
    partial void OnIndexChanged(int oldValue, int newValue)
    {
        MinusCommand.Notify(); // 刷新 MinusCommand 的可执行性
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

    // 默认命令：自动取名、不校验可执行性、排队执行
    [VeloxCommand(name: "Auto", canValidate: false, semaphore: 1)]
    private Task Plus(object? sender, CancellationToken ct)
    {
        Index++;
        Greeting = $"current index: {Index}";
        return Task.CompletedTask;
    }

    // 开启可执行性校验
    [VeloxCommand(canValidate: true)]
    private Task Minus(object? sender, CancellationToken ct)
    {
        Index--;
        Greeting = $"current index: {Index}";
        return Task.CompletedTask;
    }
    // 这个 partial 方法必须在此实现
    private partial bool CanExecuteMinusCommand(object? sender)
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

    private partial bool CanExecuteRemoveSelectedItemCommand(object? sender)
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

    private partial bool CanExecuteMoveLastToFirstCommand(object? sender)
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

    // 非阻塞式中断
    private void FreeCommand()
    {
        MinusCommand.Lock();   // 进入锁定：阻止新命令触发，但不中断正在运行的命令

        MinusCommand.Interrupt();    // 中断当前命令
        MinusCommand.Clear();        // 中断当前命令与所有排队命令

        MinusCommand.Unlock(); // 解除锁定
    }

    // 可等待式中断
    private async Task FreeCommandAsync()
    {
        MinusCommand.Lock();   // 进入锁定：阻止新命令触发，但不中断正在运行的命令

        await MinusCommand.InterruptAsync();    // 中断当前命令
        await MinusCommand.ClearAsync(); // 中断当前命令与所有排队命令

        MinusCommand.Unlock(); // 解除锁定
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