# 快速开始

通过本教程快速上手 VeloxDev。

## 安装

```shell
dotnet add package VeloxDev.Avalonia  # WPF / WinUI / MAUI / WinForms
```

## Hello Workflow

```csharp
var controller = new ControllerViewModel();
var node = new MyNodeViewModel();
WorkflowLinkViewModel.Connect(controller.Slots[0], node.Slots[0]);

var compiler = new WorkflowCompiler();
var plan = compiler.Compile(controller, CompileMode.BFS)[0];
await plan.ExecuteAsync(NetworkFlowContext.Create("payload"));
```
