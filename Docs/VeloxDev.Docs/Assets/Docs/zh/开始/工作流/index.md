# 工作流

## 简介

流程化与自动化，这在现代软件中很常见，特别是在业务流水线与设备控制流场景。现在，你可以在.NET优雅地构建易于扩展的运行时工作流，并且，随人工智能发展，令智能体理解内存对象并操作内存对象这件事已经不再遥远，您需要的，是创意与审批，而非大量的代码编写

> **实机效果**

Avalonia / WPF / WinUI / MAUI / Winforms 均已实现下述效果

![](avares://VeloxDev.Docs/Assets/Images/workflow.png)

## 优势

> **基于 MVVM，无任何 GUI 依赖**

您可在喜欢的GUI框架中绘制可交互的工作流编排视图，也可在无UI的服务器上运行已编排的工作流，VeloxDev.Core.Extension 已提供持久化服务来支持在这些模式中共享相同的数据

> **内置虚拟化**

基于网格哈希算法实现节点的空间索引与查询，实测环境可实现微秒级响应

> **智能体接管**

工作流框架已深度集成 MAF 提供的 Function Calling 能力，您的智能体现在可以在运行时理解、操作工作流

> **先进的解耦架构**

WorkflowBuilder 提供源代码生成能力，只需标注该特性就可无继承地实现工作流组件内核

WorkflowHelper 提供工作流组件所需的逻辑代码，它们在生成阶段被注入到视图模型并且支持动态替换，基于重写 Helper 内置的函数、订阅 Helper 内置的事件实现高可扩展架构

![](avares://VeloxDev.Docs/Assets/Images/workflow_framework.png)