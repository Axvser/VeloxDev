# VeloxDev

当您在 .NET 平台使用诸如 WPF / Avalonia 等框架开发带 UI 程序时，此项目可为一些功能提供更简单的代码实现。举个例子，VeloxDev 为 WPF / Avalonia 等框架提供了统一的 Fluent API 用以构建插值过渡，您将以几乎为零的学习成本掌握如何使用 C# 代码在多个平台加载插值过渡而无需关注 XAML

---

## VeloxDev.Core

> VeloxDev.Core 为 VeloxDev 搭建核心框架，包含一切必要的抽象，对于其中跨平台不变的部分进行了具体实现。实际使用时无需安装此项目，而是安装相应平台的 VeloxDev.×××

- ### Core

  - ⌈ AspectOriented ⌋ , 你的属性或方法将能被拦截或动态编辑
  - ⌈ TransitionSystem ⌋ , 跨框架一致的过渡系统

- ### Auxiliary

  - ⌈ ObjectPool ⌋ , 你将使用对象池模式提升特定场景下的程序性能
  - ⌈ WeakTypes ⌋ , 你将使用一些基于弱类型的工具以降低内存泄漏风险

---

## VeloxDev

> 若不属于 WPF / Avalonia / MAUI 中的任何一个，请选择此项目


## VeloxDev.WPF

> WPF 请选择此项目

## VeloxDev.Avalonia

> Avalonia 请选择此项目

## VeloxDev.MAUI

> MAUI 请选择此项目