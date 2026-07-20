# Quick Start

This guide walks you through building your first VeloxDev project step by step.

---

## Prerequisites

- .NET SDK 8.0 or later
- Visual Studio 2022 (or JetBrains Rider) with the workload matching your target framework
- **Optional**: Install the VeloxDev templates for CLI scaffolding:

```shell
dotnet new install VeloxDev.Workflow.Templates
```

---

## 1. Create a New Project

Choose your preferred GUI framework:

```shell
# Avalonia (Desktop + Browser)
dotnet new avalonia.app -n MyWorkflowApp

# WPF
dotnet new wpf -n MyWorkflowApp

# WinUI / MAUI / WinForms — same pattern
```

## 2. Install the Adapter Package

```shell
dotnet add package VeloxDev.Avalonia     # or .WPF / .WinUI / .MAUI / .WinForms
```

If you need intelligent agent or serialization capabilities, also install:

```shell
dotnet add package VeloxDev.Core.Extension
```

## 3. Define Your First Node

Create a ViewModel for a workflow node — the source generator will create the property-change notification automatically:

```csharp
using VeloxDev.MVVM;
using VeloxDev.WorkflowSystem;

public partial class MyNodeViewModel : WorkflowNodeViewModel
{
	[VeloxProperty] private string _label = "My Node";
	[VeloxProperty] private int _value;
}
```

## 4. Register the Workflow Surface

In your XAML view, add the workflow surface behavior and declare the Tree component:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
			 xmlns:behaviors="using:VeloxDev.WorkflowSystem.AttachedBehaviors"
			 behaviors:WorkflowSurfaceBehavior.IsEnabled="True">
	<framework:WorkflowTreeView x:Name="TreeView" />
</UserControl>
```

## 5. Connect and Run

```csharp
// In your ViewModel constructor
var controller = new ControllerViewModel();
var node1 = new MyNodeViewModel();
var link = WorkflowLinkViewModel.Connect(controller.Slots[0], node1.Slots[0]);
```

> **Next steps**: Head to **QuickStart → Hello Workflow** for a complete walk‑through, or dive directly into **DeepDive → Workflow Engine** to understand the architecture.
