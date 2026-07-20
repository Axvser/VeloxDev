# Workflow

## Introduction

Process orientation and automation are common in modern software, especially in business pipelines and device control flow scenarios. Now, you can elegantly build easily extensible runtime workflows in .NET, and with the advancement of artificial intelligence, it is no longer far off for agents to understand and manipulate in-memory objects. What you need is creativity and approval, rather than massive code writing.

> **Actual device effect**

Avalonia / WPF / WinUI / MAUI / Winforms have all implemented the following effects.

![](avares://VeloxDev.Docs/Assets/Images/workflow.png)

## Advantage

> **Based on MVVM, no GUI dependencies**

You can draw interactive workflow orchestration views in your favorite GUI framework, or run orchestrated workflows on a UI-less server. VeloxDev.Core.Extension already provides persistence services to support sharing the same data across these modes.

> **Built-in virtualization**

Implement spatial indexing and querying of nodes based on the grid hash algorithm, achieving microsecond-level response in actual test environments.

> **Agent Takeover**

The workflow framework has deeply integrated the Function Calling capability provided by MAF, allowing your intelligent agent to understand and operate workflows at runtime.

> **Advanced Decoupled Architecture**

WorkflowBuilder provides source code generation capability. Simply by annotating this feature, you can implement the workflow component kernel without inheritance.

WorkflowHelper provides the logic code required by workflow components. It is injected into the view model during the generation phase and supports dynamic replacement. By overriding the built-in functions of Helper and subscribing to the built-in events of Helper, a highly extensible architecture is achieved.

![](avares://VeloxDev.Docs/Assets/Images/workflow_framework.png)