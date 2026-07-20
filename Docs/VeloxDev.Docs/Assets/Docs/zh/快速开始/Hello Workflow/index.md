# Hello Workflow

五分钟构建并运行第一个工作流。

## 定义节点

```csharp
public partial class StartNode : WorkflowNodeViewModel
{
    [VeloxProperty] private string _message = "你好！";
}
```

## 连接并编译

```csharp
var controller = new ControllerViewModel();
var start = new StartNode();
WorkflowLinkViewModel.Connect(controller.Slots[0], start.Slots[0]);

var results = new WorkflowCompiler().Compile(controller, CompileMode.BFS);
await results[0].ExecuteAsync(NetworkFlowContext.Create("payload"));
```
