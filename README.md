# VeloxDev

[![GitHub](https://img.shields.io/badge/GitHub-Repository-blue?logo=github)](https://github.com/Axvser/VeloxDev)  

[![GitHub](https://img.shields.io/badge/GitHub-Example-cyan?logo=github)](https://github.com/Axvser/VeloxDev/tree/master/Examples)  

在 Avalonia / WPF … 中使用完全一致的 API 完成任务 ！

> 在.NET平台，几乎任何UI框架都有自身独特的属性、动画等系统<br>我们如果想在现有基础上学习新的UI框架，通常需要耗费以周为单位的时间，这是一笔不小的时间支出

> 不过，基于以下几点，我们其实可以将一些功能实现变为多框架一致的<br>Ⅰ 标准CLR属性 ➤ 现代UI框架无论自身属性系统如何，最终会以标准CLR属性暴露给用户<br>Ⅱ MVVM一致性 ➤ 现代UI框架均能基于.NET标准接口支持MVVM<br>Ⅲ Roslyn平台 ➤ .NET最强大的特性之一，通过自动分析并生成代码消除运行时开销、加速项目开发<br>Ⅳ XAML概念 ➤ 就像会WPF的开发者可以在2天内入门Avalonia，XAML是一个抽象的、宽泛的概念，XAML语法的细小差异通常不会成为移植的难点

---

# VeloxDev.Core

[![NuGet](https://img.shields.io/nuget/v/VeloxDev.Core?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Core/)

> 定义一组核心接口与抽象类，它们是实现多框架统一的关键，自身包含一个源代码生成器，可以直接实现一些不需要做平台适配的功能模块

# Core
  - ⌈ MVVM Toolkit ⌋ , 自动化属性生成与命令生成 ✔
  - ⌈ Workflow ⌋ ，拖拽式工作流构建器 ✔
  - ⌈ Transition ⌋ , 使用Fluent API构建过渡效果 ✔ （ 依赖平台特定适配层 ）
  - ⌈ ThemeManager ⌋ , 仅需一个特性标记即可实现主题切换 ✔ （ 依赖平台特定适配层 ）
  - ⌈ AspectOriented ⌋ , 动态拦截/编辑属性、方法调用 ✔ （ 限 .NET5 + ）
  - ⌈ MonoBehaviour ⌋ , 实时帧刷新行为 ✔

# Product

> 我们已经做过一些适配层，对 WPF / Avalonia 的支持比较完善，您可直接使用它们，或者参考其源码来实现属于您自己的平台适配层

### VeloxDev.WPF [![NuGet](https://img.shields.io/nuget/v/VeloxDev.WPF?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.WPF/)

### VeloxDev.Avalonia [![NuGet](https://img.shields.io/nuget/v/VeloxDev.Avalonia?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Avalonia/)

### VeloxDev.MAUI  [![NuGet](https://img.shields.io/nuget/v/VeloxDev.MAUI?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.MAUI/)

---