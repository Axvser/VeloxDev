using System.Collections.ObjectModel;
using VeloxDev.MVVM;

namespace Demo.ViewModels;

/// <summary>One row of the MVVM card's task list.</summary>
public sealed class TaskBoardItem
{
    /// <summary>Creates a row with the given title.</summary>
    /// <param name="title">The text shown in the row.</param>
    public TaskBoardItem(string title) => Title = title;

    /// <summary>The row's title.</summary>
    public string Title { get; }

    /// <inheritdoc />
    public override string ToString() => Title;
}

/// <summary>
/// The MVVM card's view model. <c>[VeloxProperty]</c> promotes the fields to notifying properties;
/// <c>[VeloxCommand]</c> turns the methods into commands the XAML binds to.
/// </summary>
public sealed partial class TaskBoardViewModel
{
    [VeloxProperty] private string _draft = string.Empty;
    [VeloxProperty] private TaskBoardItem? _selected;
    [VeloxProperty] private string _status = "Ready";

    /// <summary>Creates the sample rows and selects the first one.</summary>
    public TaskBoardViewModel()
    {
        Tasks.Add(new TaskBoardItem("Bind Text to Draft"));
        Tasks.Add(new TaskBoardItem("Run AddCommand"));
        Tasks.Add(new TaskBoardItem("Remove drops the selected row"));
        Selected = Tasks[0];
        Status = $"{Tasks.Count} tasks · select one, then Remove";
    }

    /// <summary>The rows; the list box tracks this observable collection.</summary>
    public ObservableCollection<TaskBoardItem> Tasks { get; } = [];

    [VeloxCommand(canValidate: true)]
    private void Add()
    {
        var title = Draft.Trim();
        if (title.Length == 0)
        {
            Status = "Type a title first.";
            return;
        }

        var item = new TaskBoardItem(title);
        Tasks.Add(item);
        Selected = item;
        Draft = string.Empty;
        Status = $"Added '{title}' · {Tasks.Count} tasks";
    }

    private partial bool CanExecuteAddCommand(object? parameter) => !string.IsNullOrWhiteSpace(_draft);

    partial void OnDraftChanged(string oldValue, string newValue) => AddCommand.Notify();

    [VeloxCommand(canValidate: true)]
    private void Remove()
    {
        if (Selected is null)
        {
            Status = "Select a task first.";
            return;
        }

        var title = Selected.Title;
        Tasks.Remove(Selected);
        Selected = Tasks.Count > 0 ? Tasks[0] : null;
        Status = $"Removed '{title}' · {Tasks.Count} tasks";
    }

    private partial bool CanExecuteRemoveCommand(object? parameter) => _selected is not null;

    partial void OnSelectedChanged(TaskBoardItem? oldValue, TaskBoardItem? newValue) => RemoveCommand.Notify();

    [VeloxCommand]
    private void Clear()
    {
        Tasks.Clear();
        Selected = null;
        Status = "Cleared";
    }
}
