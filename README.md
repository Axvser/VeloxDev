# VeloxDev

[![GitHub](https://img.shields.io/badge/GitHub-Repository-blue?logo=github)](https://github.com/Axvser/VeloxDev)  

当您在 .NET 平台使用诸如 WPF / Avalonia 等框架开发带 UI 程序时，此项目可为一些功能提供更简单、更统一的代码实现

> 举个例子，VeloxDev 为 WPF / Avalonia 等框架提供了统一的 Fluent API 用以构建插值过渡，您将以几乎为零的学习成本掌握如何使用 C# 代码在多个平台加载插值过渡而无需关注 XAML

---

# VeloxDev.Core

[![NuGet](https://img.shields.io/nuget/v/VeloxDev.Core?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Core/)

> VeloxDev.Core 是 VeloxDev 框架核心，包含一切必要的抽象，对于其中跨平台不变的部分进行了抽象类实现。实际使用时无需安装此项目，而是安装相应平台的 VeloxDev.×××

# Core
  - ⌈ TransitionSystem ⌋ , 使用Fluent API构建过渡效果 ✔
  - ⌈ AspectOriented ⌋ , 动态拦截/编辑属性、方法调用 ✔
  - ⌈ MonoBehaviour ⌋ , 实时帧刷新行为 ✔
  - ⌈ Visual Workflow Builder ⌋ ，拖拽式工作流构建器 ❌ 【 预计 V2 实装此项 】

⌈ …… ⌋ 考虑到作者大学快毕业了，因此这个项目可能需要更多合作者，这样才能确保项目持续开发，当然，如果作者能找到相关工作，那是肯定不会断更的

# VeloxDev.×××

### VeloxDev.WPF 

[![NuGet](https://img.shields.io/nuget/v/VeloxDev.WPF?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.WPF/)

### VeloxDev.Avalonia

[![NuGet](https://img.shields.io/nuget/v/VeloxDev.Avalonia?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.Avalonia/)

### VeloxDev.MAUI

[![NuGet](https://img.shields.io/nuget/v/VeloxDev.MAUI?color=green&logo=nuget)](https://www.nuget.org/packages/VeloxDev.MAUI/)

---

# API

> VeloxDev.××× 是作者基于 VeloxDev.Core 实现的简易封装 , 可在 WPF / Avalonia / MAUI 使用统一的 API 去实现属性值过渡、AOP编程等功能

## Ⅰ AOP编程

> 对于公开可读写的属性或者公开可调用的方法,我们借助源生成器的力量即可对其动态代理,接着,这些属性或方法将能被我们拦截

```csharp
    public partial class MyClass
    {
        [AspectOriented] // 标记为可AOP编程的,源生成器将产生动态代理Proxy属性
        public void SaveData()
        {

        }

        public void SetProxy() // 可以在运行时动态编辑Proxy的行为,并通过Proxy访问,即 Proxy.SaveData()
        {
            Proxy.SetMethod(nameof(SaveData),
            (paras, prevs) =>
            {
                MessageBox.Show("拦截，发生在方法调用前");
                return null;
            },
            (paras, prevs) =>
            {
                MessageBox.Show("覆盖，不再使用方法的原始逻辑");
                return null;
            },
            (paras, prevs) =>
            {
                MessageBox.Show("回调，发生在方法调用后");
                return null;
            });

            // paras : 本次方法接收的参数
            // prevs : 上一个节点的返回值
        }
    }
```

## Ⅱ 过渡

> WPF / Avalonia / MAUI 虽然各自使用不同的属性系统,但最终都会以标准CLR属性暴露给用户,基于这一特点,我们可以使用下述API来实现跨平台一致的动画创建

- 线程安全 ✔
- 生命周期支持 ✔
- 并行与预编译 ❌ 【 预计 V2 实装此项 】

```csharp
            var transition = Transition.Create(this)
                .Await(TimeSpan.FromSeconds(3))// (可选) 等待 3s 后执行第一段动画
                .Property(x => x.Background, Brushes.Red)
                .Property(x => x.Opacity, 0.5d)
                .Effect(TransitionEffects.Theme) // 效果参数
                .Then() // 执行下一段动画 > (可选) AwaitThen()以延迟启动下一段动画
                .Property(x => x.Background, Brushes.Cyan)
                .Property(x => x.Opacity, 1d)
                .Effect((p) =>
                {
                    p.Duration = TimeSpan.FromSeconds(1);
                    p.Awaked += (s, e) =>
                    {

                    };
                    p.Update += (s, e) =>
                    {

                    };
                }); // 使用自定义的效果参数
            transition.Start();
```