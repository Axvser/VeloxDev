# 附加行为

若您已经阅读过组件构建与业务逻辑注入，那么现在，工作流已经可以运行

本章节解决的问题是：如何基于附加行为实现工作流 GUI

本章节适用于 Avalonia / WPF / WinUI / MAUI（ Winfroms 无附加属性机制，只是API相似 ，实际推荐的做法是直接从仓库的 Demo 移植；Razor 还在实验阶段 ）

> **引用**

```xml
<UserControl xmlns:behaviors="using:VeloxDev.WorkflowSystem.AttachedBehaviors" />
```