using System.Windows.Input;
using VeloxDev.AI;

namespace VeloxDev.Core.Test.AI;

[TestClass]
public class AgentCommandDiscovererTests
{
    private sealed class FakeCommand(Action<object?> execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute(parameter);
    }

    private sealed class ViewModel
    {
        [AgentContext(AgentLanguages.English, "Saves data")]
        public ICommand SaveCommand { get; } = new FakeCommand(_ => { });

        [AgentContext(AgentLanguages.English, "Deletes data")]
        public ICommand DeleteCommand { get; } = new FakeCommand(_ => { });

        public string NotACommand { get; set; } = "";
    }

    [TestMethod]
    public void DiscoverCommands_FindsICommandProperties()
    {
        var vm = new ViewModel();
        var cmds = AgentCommandDiscoverer.DiscoverCommands(vm);
        Assert.IsTrue(cmds.Any(c => c.Name == "SaveCommand"));
        Assert.IsTrue(cmds.Any(c => c.Name == "DeleteCommand"));
    }

    [TestMethod]
    public void DiscoverCommands_ExcludesNonCommands()
    {
        var vm = new ViewModel();
        var cmds = AgentCommandDiscoverer.DiscoverCommands(vm);
        Assert.IsFalse(cmds.Any(c => c.Name == "NotACommand"));
    }

    [TestMethod]
    public void DiscoverCommands_NullTarget_ReturnsEmpty()
    {
        var cmds = AgentCommandDiscoverer.DiscoverCommands(null!);
        Assert.AreEqual(0, cmds.Count);
    }

    [TestMethod]
    public void DiscoverCommands_ReadsAgentContext()
    {
        var vm = new ViewModel();
        var cmds = AgentCommandDiscoverer.DiscoverCommands(vm, AgentLanguages.English);
        var save = cmds.First(c => c.Name == "SaveCommand");
        CollectionAssert.Contains((System.Collections.ICollection)save.AgentDescriptions, "Saves data");
    }

    [TestMethod]
    public void Execute_ValidCommand_Succeeds()
    {
        object? received = null;
        var vm = new { TestCommand = (ICommand)new FakeCommand(p => received = p) };
        // Use the ViewModel class instead, since anonymous types won't work well
        var result = AgentCommandDiscoverer.Execute(
            new ViewModelWithAction(p => received = p),
            "Action");
        Assert.IsTrue(result.Success);
        Assert.IsNull(received); // null parameter
    }

    [TestMethod]
    public void Execute_NonExistentCommand_Fails()
    {
        var vm = new ViewModel();
        var result = AgentCommandDiscoverer.Execute(vm, "NonExistent");
        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.Error);
    }

    [TestMethod]
    public void Execute_NullTarget_Fails()
    {
        var result = AgentCommandDiscoverer.Execute(null!, "Save");
        Assert.IsFalse(result.Success);
    }

    [TestMethod]
    public void CanExecuteCommand_NullTarget_ReturnsFalse()
    {
        Assert.IsFalse(AgentCommandDiscoverer.CanExecuteCommand(null!, "Save"));
    }

    [TestMethod]
    public void FindBackingCommand_ExistingCommand_ReturnsName()
    {
        var result = AgentCommandDiscoverer.FindBackingCommand(typeof(ViewModel), "Save");
        Assert.AreEqual("SaveCommand", result);
    }

    [TestMethod]
    public void FindBackingCommand_NoMatch_ReturnsNull()
    {
        var result = AgentCommandDiscoverer.FindBackingCommand(typeof(ViewModel), "Ghost");
        Assert.IsNull(result);
    }

    private sealed class ViewModelWithAction
    {
        public ICommand ActionCommand { get; }
        public ViewModelWithAction(Action<object?> action) => ActionCommand = new FakeCommand(action);
    }
}
