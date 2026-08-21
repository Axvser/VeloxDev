using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VeloxDev.MVVM;

namespace Demo.ViewModels;

/* No need to inherit any class, and no need to explicitly declare an interface */
/* Tip: you can inherit other classes, but avoid MVVM-related ones, since this toolkit already
   provides complete MVVM support and inheriting another MVVM base may conflict with it. */
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

    /* Quickly generate your properties */
    [VeloxProperty] private int _index = 0;
    [VeloxProperty] private string _greeting = $"current index: 0";
    [VeloxProperty] private ObservableCollection<string> _items = [];
    [VeloxProperty] private string? _selectedItem;
    [VeloxProperty] private string _selectedItemSummary = "当前选中: (无)";
    [VeloxProperty] private string _collectionStatus = "等待集合通知";
    [VeloxProperty] private string _collectionTrace = "OnCollectionChanged<T> 尚未触发";

    /* Property callbacks */
    partial void OnIndexChanged(int oldValue, int newValue)
    {
        MinusCommand.Notify(); // notify that MinusCommand's executability needs to be refreshed
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

    /* A default Command with an auto-derived name, no executability validation, queued execution */
    [VeloxCommand(name: "Auto", canValidate: false, semaphore: 1)]
    private Task Plus(object? sender, CancellationToken ct)
    {
        Index++;
        Greeting = $"current index: {Index}";
        return Task.CompletedTask;
    }

    /* Enable executability validation */
    [VeloxCommand(canValidate: true)]
    private Task Minus(object? sender, CancellationToken ct)
    {
        Index--;
        Greeting = $"current index: {Index}";
        return Task.CompletedTask;
    }
    /* This partial method must be implemented at this point */
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

    /* Non-blocking interrupt */
    private void FreeCommand()
    {
        MinusCommand.Lock();   // enter the locked state: prevents new commands from triggering but
                               // does not interrupt the currently running command

        MinusCommand.Interrupt();    // interrupt the current command
        MinusCommand.Clear();        // interrupt the current command and all queued commands

        MinusCommand.UnLock(); // release the lock
    }

    /* Awaitable interrupt */
    private async Task FreeCommandAsync()
    {
        MinusCommand.Lock();   // enter the locked state: prevents new commands from triggering but
                               // does not interrupt the currently running command

        await MinusCommand.InterruptAsync();    // interrupt the current command
        await MinusCommand.ClearAsync(); // interrupt the current command and all queued commands

        MinusCommand.UnLock(); // release the lock
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