using Demo.ViewModels;
using System.Collections.ObjectModel;
using VeloxDev.Core.Interfaces.WorkflowSystem;
using VeloxDev.Core.WorkflowSystem;
using VeloxSize = VeloxDev.Core.WorkflowSystem.Size;

namespace Demo.Workflow;

public sealed class WorkflowDemoSession
{
    private WorkflowDemoSession(TreeViewModel tree, ControllerViewModel controller, IEnumerable<NodeViewModel> nodes)
    {
        Tree = tree;
        Controller = controller;
        Nodes = new ObservableCollection<NodeViewModel>(nodes);
    }

    public TreeViewModel Tree { get; }
    public ControllerViewModel Controller { get; }
    public ObservableCollection<NodeViewModel> Nodes { get; }

    public static WorkflowDemoSession Create()
    {
        var tree = new TreeViewModel();
        var helper = tree.GetHelper();
        const double nodeWidth = 220;
        const double nodeHeight = 180;
        const double controllerWidth = 240;
        const double controllerHeight = 170;

        NodeViewModel CreateNode(
            string title,
            NetworkRequestMethod method,
            string url,
            string captureKey,
            double left,
            double top,
            string headers = "",
            string bodyTemplate = "")
            => new()
            {
                Title = title,
                Method = method,
                Url = url,
                Headers = headers,
                BodyTemplate = bodyTemplate,
                CaptureKey = captureKey,
                Size = new VeloxSize(nodeWidth, nodeHeight),
                Anchor = new Anchor(left, top, 1),
            };

        var controller = new ControllerViewModel
        {
            Size = new VeloxSize(controllerWidth, controllerHeight),
            Anchor = new Anchor(140, 220, 1),
            SeedPayload = "demo-request-chain",
            BroadcastMode = WorkflowBroadcastMode.BreadthFirst,
        };

        var fetchTodo = CreateNode("Fetch Todo", NetworkRequestMethod.Get, "https://jsonplaceholder.typicode.com/todos/1", "todo", 420, 60);
        var fetchPost = CreateNode("Fetch Post", NetworkRequestMethod.Get, "https://jsonplaceholder.typicode.com/posts/1", "post", 420, 300);
        var auditTodo = CreateNode("Audit Todo", NetworkRequestMethod.Post, "https://httpbin.org/post", "audit", 710, 20, headers: "X-Demo-Source: VeloxDev Workflow", bodyTemplate: "{\"todoSummary\":\"{{todo.summary}}\",\"todoStatus\":\"{{todo.status}}\"}");
        var patchRemote = CreateNode("Patch Remote", NetworkRequestMethod.Patch, "https://httpbin.org/patch", "patch", 710, 180, bodyTemplate: "{\"todoUrl\":\"{{todo.url}}\",\"status\":\"processed\"}");
        var syncPost = CreateNode("Sync Post", NetworkRequestMethod.Post, "https://httpbin.org/post", "sync", 710, 340, bodyTemplate: "{\"postUrl\":\"{{post.url}}\",\"summary\":\"{{post.summary}}\"}");
        var deleteRemote = CreateNode("Delete Remote", NetworkRequestMethod.Delete, "https://httpbin.org/delete", "delete", 710, 500, headers: "X-Delete-Reason: {{todo.status}}");
        var archiveTrace = CreateNode("Archive Trace", NetworkRequestMethod.Post, "https://httpbin.org/post", "archive", 1000, 220, bodyTemplate: "{\"last\":\"{{last.summary}}\",\"seed\":\"{{seed}}\"}");

        foreach (var node in new IWorkflowNodeViewModel[]
        {
            controller,
            fetchTodo,
            fetchPost,
            auditTodo,
            patchRemote,
            syncPost,
            deleteRemote,
            archiveTrace,
        })
        {
            helper.CreateNode(node);
        }

        controller.OutputSlot = CreateOutputSlot(controllerWidth, controllerHeight, SlotChannel.MultipleTargets);
        fetchTodo.InputSlot = CreateInputSlot(nodeHeight);
        fetchTodo.OutputSlot = CreateOutputSlot(nodeWidth, nodeHeight, SlotChannel.MultipleTargets);
        fetchPost.InputSlot = CreateInputSlot(nodeHeight);
        fetchPost.OutputSlot = CreateOutputSlot(nodeWidth, nodeHeight, SlotChannel.MultipleTargets);
        auditTodo.InputSlot = CreateInputSlot(nodeHeight);
        auditTodo.OutputSlot = CreateOutputSlot(nodeWidth, nodeHeight, SlotChannel.OneTarget);
        patchRemote.InputSlot = CreateInputSlot(nodeHeight);
        patchRemote.OutputSlot = CreateOutputSlot(nodeWidth, nodeHeight, SlotChannel.OneTarget);
        syncPost.InputSlot = CreateInputSlot(nodeHeight);
        syncPost.OutputSlot = CreateOutputSlot(nodeWidth, nodeHeight, SlotChannel.OneTarget);
        deleteRemote.InputSlot = CreateInputSlot(nodeHeight);
        deleteRemote.OutputSlot = CreateOutputSlot(nodeWidth, nodeHeight, SlotChannel.OneTarget);
        archiveTrace.InputSlot = CreateInputSlot(nodeHeight, SlotChannel.MultipleSources);

        Connect(tree, controller.OutputSlot!, fetchTodo.InputSlot!);
        Connect(tree, controller.OutputSlot!, fetchPost.InputSlot!);
        Connect(tree, fetchTodo.OutputSlot!, auditTodo.InputSlot!);
        Connect(tree, fetchTodo.OutputSlot!, patchRemote.InputSlot!);
        Connect(tree, fetchPost.OutputSlot!, syncPost.InputSlot!);
        Connect(tree, fetchPost.OutputSlot!, deleteRemote.InputSlot!);
        Connect(tree, auditTodo.OutputSlot!, archiveTrace.InputSlot!);
        Connect(tree, patchRemote.OutputSlot!, archiveTrace.InputSlot!);
        Connect(tree, syncPost.OutputSlot!, archiveTrace.InputSlot!);
        Connect(tree, deleteRemote.OutputSlot!, archiveTrace.InputSlot!);

        helper.ClearHistory();
        return new WorkflowDemoSession(tree, controller, [fetchTodo, fetchPost, auditTodo, patchRemote, syncPost, deleteRemote, archiveTrace]);
    }

    private static SlotViewModel CreateInputSlot(double nodeHeight, SlotChannel channel = SlotChannel.OneSource)
        => new()
        {
            Offset = new Offset(0, (nodeHeight - 20) / 2),
            Size = new VeloxSize(20, 20),
            Channel = channel,
        };

    private static SlotViewModel CreateOutputSlot(double nodeWidth, double nodeHeight, SlotChannel channel)
        => new()
        {
            Offset = new Offset(nodeWidth - 20, (nodeHeight - 20) / 2),
            Size = new VeloxSize(20, 20),
            Channel = channel,
        };

    private static void Connect(IWorkflowTreeViewModel tree, IWorkflowSlotViewModel sender, IWorkflowSlotViewModel receiver)
    {
        tree.GetHelper().SendConnection(sender);
        tree.GetHelper().ReceiveConnection(receiver);
    }
}
