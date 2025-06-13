# VeloxDev

当您在 .NET 平台使用诸如 WPF / Avalonia 等框架开发带 UI 程序时，此项目可为一些功能提供更简单的代码实现。

> 举个例子，VeloxDev 为 WPF / Avalonia 等框架提供了统一的 Fluent API 用以构建插值过渡，您将以几乎为零的学习成本掌握如何使用 C# 代码在多个平台加载插值过渡而无需关注 XAML

---

# VeloxDev.Core

> VeloxDev.Core 是 VeloxDev 框架核心，包含一切必要的抽象，对于其中跨平台不变的部分进行了抽象类实现。实际使用时无需安装此项目，而是安装相应平台的 VeloxDev.×××

- ## Core
  - ⌈ TransitionSystem ⌋ , 使用Fluent API构建过渡效果
  - ⌈ AspectOriented ⌋ , 动态拦截/编辑属性、方法调用

- ## VeloxDev.×××

  - ### VeloxDev.NET

  - ### VeloxDev.WPF

  - ### VeloxDev.Avalonia

  - ### VeloxDev.MAUI