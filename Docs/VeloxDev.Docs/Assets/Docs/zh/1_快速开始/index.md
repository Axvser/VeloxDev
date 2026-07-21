# 快速开始

通过本教程快速上手 VeloxDev。

## 安装

```shell
# 根据你的框架选择
dotnet add package VeloxDev.Avalonia  # WPF / WinUI / MAUI / WinForms / Razor
```

## 定义节点

```csharp
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

[WorkflowBuilder.Node<NodeHelper>]
public partial class MyNodeViewModel
{
    public MyNodeViewModel() => InitializeWorkflow();

    [VeloxProperty] private string _label = "My Node";
    [VeloxProperty] private int _value;
}
```

## Hello Workflow

```csharp
var tree = new TreeDefaultViewModel();
var controller = new ControllerNode();
var node = new MyNodeViewModel();
tree.Nodes.Add(controller);
tree.Nodes.Add(node);

// 连接
var link = new LinkDefaultViewModel
{
    Sender = controller.Slots[0],
    Receiver = node.Slots[0]
};
tree.Links.Add(link);

// 编译并执行
var compiler = new WorkflowCompiler();
var plan = compiler.Compile(controller, CompileMode.BFS)[0];
await plan.ExecuteAsync("payload");
```
