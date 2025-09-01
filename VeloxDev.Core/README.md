# VeloxDev.Core

---

[![GitHub](https://img.shields.io/badge/GitHub-Repository-blue?logo=github)](https://github.com/Axvser/VeloxDev/tree/master/VeloxDev.Core)

[![GitHub](https://img.shields.io/badge/GitHub-Example-cyan?logo=github)](https://github.com/Axvser/VeloxDev/tree/master/Examples)

## Ⅰ MVVM Toolkit

> 标记 [ VeloxProperty ] 与 [ VeloxCommand ] 以更快构建 ViewModel

> 注意 : 这将生成 MVVM 接口实现，与其它 MVVM 工具混用可能导致生成内容重复

```csharp
    public sealed partial class SlotContext
    {
        [VeloxProperty]
        private string name = string.Empty;

        partial void OnNameChanged(string oldValue,string newValue)
        {
            DeleteCommand.Notify(); // 通知命令可执行态的改变
        }

        [VeloxCommand]
        private Task Delete(object? parameter, CancellationToken ct)
        {
            // …… 此处执行你的命令逻辑
            // 可在 VeloxCommand 的参数中选择 Command名称，默认 “Auto”
            // 可在 VeloxCommand 的参数中选择是否手动验证命令可执行性
            // 可在 VeloxCommand 的参数中选择信号量以启用并发
            return Task.CompletedTask;
        }

        private void Test()
        {
            // 下述三个方法均有 Async 版本
            DeleteCommand.Execute(null); // 执行
            DeleteCommand.Cancel();      // 取消当前执行中的 Task
            DeleteCommand.Interrupt();   // 取消包含排队 Task 在内的所有 Task
            
            DeleteCommand.Lock();        // 锁定命令以阻止所有任务继续
            DeleteCommand.UnLock();      // 解锁命令
        }
    }
```

---

## Ⅱ Workflow

> 思维导图、流程控制、电路模拟 …… 等诸多场景都会要求有可拖拽的流程编辑器。VeloxDev 提供了纯 MVVM 模式的功能实现，并且支持源代码生成

[![GitHub](https://img.shields.io/badge/GitHub-Avalonia-cyan?logo=github)](https://github.com/Axvser/VeloxDev/tree/master/Examples/Avalonia/Workflow/Demo)  

[![GitHub](https://img.shields.io/badge/GitHub-WPF-cyan?logo=github)](https://github.com/Axvser/VeloxDev/tree/master/Examples/WPF/Workflow/Demo)  

![](https://s3.bmp.ovh/imgs/2025/07/28/824c68b88eb1f5ec.png)

### ViewModel 生成支持

> 我们的工作流是 MVVM 的，建议您使用下述特性快速生成多个包含可选项的模板

| 特性 | 应用对象 | 参数                                       | 描述 |
|------|----------|------------------------------------------|------|
| `Workflow.Context.Tree` | 工作流树类 | `slotType` - 自定义槽位<br>`linkType` - 连接线类型 | 标记自定义工作树类型 |
| `Workflow.Context.Node` | 工作流节点类 | `semaphore` - 节点最大任务并发数                  | 标记自定义节点类型 |
| `Workflow.Context.Slot` | 工作流槽位类 | 无                                        | 标记自定义槽位类型 |
| `Workflow.Context.Link` | 工作流连接线类 | 无                                        | 标记自定义连接线类型 |

### ViewModel 生成结果

> 关键的属性、命令、扩展点，在您使用源生成特性后，即可参考这些信息来扩展功能

> IWorkflowContext 是所有工作流视图模型的共有接口，所以它的一切对其它工作流视图模型均适用

| **接口** | **关键属性** | **关键命令** | **核心功能** | **回调方法** | **重要方法** |
|----------|--------------|--------------|--------------|--------------|--------------|
| **IWorkflowContext** | `IsEnabled` - 启用状态<br>`UID` - 唯一标识<br>`Name` - 显示名称 | `UndoCommand` - 撤销操作 | 基础行为控制 | `OnPropertyChanging`<br>`OnPropertyChanged`<br>`OnIsEnabledChanging/Changed`<br>`OnUIDChanging/Changed`<br>`OnNameChanging/Changed` | 无 |
| **IWorkflowLink** | `Sender` - 发送端槽位<br>`Processor` - 接收端槽位<br>`IsEnabled` - 启用状态<br>`UID` - 唯一标识<br>`Name` - 显示名称 | `DeleteCommand` - 删除连接<br>`UndoCommand` - 撤销操作 | 连接关系管理 | `OnSenderChanging/Changed`<br>`OnProcessorChanging/Changed` | 无 |
| **IWorkflowNode** | `Parent` - 所属工作流树<br>`Anchor` - 节点坐标<br>`Size` - 节点尺寸<br>`Slots` - 槽位集合<br>`IsEnabled` - 启用状态<br>`UID` - 唯一标识<br>`Name` - 显示名称 | `CreateSlotCommand` - 创建槽位<br>`DeleteCommand` - 删除节点<br>`BroadcastCommand` - 广播消息<br>`WorkCommand` - 执行节点工作任务<br>`UndoCommand` - 撤销操作 | 节点核心功能 | `OnParentChanging/Changed`<br>`OnAnchorChanging/Changed`<br>`OnSizeChanging/Changed`<br>`OnSlotAdded/Removed/Created`<br>`OnWorkExecuting/Canceled/Finished`<br>`OnFlowing/FlowCanceled/FlowFinished` | `FindLink()` - 查找连接 |
| **IWorkflowSlot** | `Parent` - 所属工作流节点<br>`Targets` - 目标节点集合<br>`Sources` - 源节点集合<br>`Capacity` - 槽位能力<br>`State` - 当前状态<br>`Anchor` - 绝对坐标<br>`Offset` - 槽位偏移坐标<br>`Size` - 槽位尺寸<br>`IsEnabled` - 启用状态<br>`UID` - 唯一标识<br>`Name` - 显示名称 | `ConnectingCommand` - 开始连接<br>`ConnectedCommand` - 完成连接<br>`DeleteCommand` - 删除槽位<br>`UndoCommand` - 撤销操作 | 槽位连接管理 | <br>`OnParentChanging/Changed`<br>`OnCapacityChanging/Changed`<br>`OnStateChanging/Changed`<br>`OnAnchorChanging/Changed`<br>`OnOffsetChanging/Changed`<br>`OnSizeChanging/Changed` | 无 |
| **IWorkflowTree** | `VirtualLink` - 虚拟连接线<br>`Nodes` - 节点集合<br>`Links` - 连接线集合<br>`IsEnabled` - 启用状态<br>`UID` - 唯一标识<br>`Name` - 显示名称 | `CreateNodeCommand` - 创建节点<br>`SetPointerCommand` - 设置触点位置<br>`SetSenderCommand` - 设置发送端<br>`SetProcessorCommand` - 设置接收端<br>`ResetStateCommand` - 重置状态<br>`UndoCommand` - 撤销操作 | 工作流树控制 | `OnNodeAdded/Removed/Created`<br>`OnLinkAdded/Removed/Created`<br>`OnPointerChanging/Changed`<br>`OnNodeReseting/Reseted`<br>`OnLinkReseted` | `PushUndo()` - 压入撤销操作<br>`FindLink()` - 查找连接 |

---

## Ⅲ 过渡

> WPF / Avalonia / MAUI 虽然各自使用不同的属性系统,但最终都会以标准CLR属性暴露给用户,基于这一特点,我们可以使用下述API来实现跨平台一致的动画创建

[![GitHub](https://img.shields.io/badge/GitHub-Avalonia-cyan?logo=github)](https://github.com/Axvser/VeloxDev/tree/master/Examples/Avalonia/Transition/Demo)  

[![GitHub](https://img.shields.io/badge/GitHub-WPF-cyan?logo=github)](https://github.com/Axvser/VeloxDev/tree/master/Examples/WPF/Transition/Demo)  

```csharp
                var effect1 = new TransitionEffect()
                {
                    Duration = TimeSpan.FromSeconds(2),
                    LoopTime = 1,
                    FPS = 144,
                    Ease = Eases.Cubic.InOut,
                };

                effect1.Completed += (s, e) =>
                {
                    MessageBox.Show("Animation Completed");
                };

                var animation = Transition<Window>.Create()
                    .Property(w => w.Background, Brushes.Violet)
                    .Effect(effect1)
                    .Then()
                    .Property(w => w.Background, Brushes.Lime)
                    .Effect(effect1)
                    .Execute(this);

                // Transition<Window>.Execute(this);
```

## Ⅳ AOP编程

> 对于公开可读写的属性或者公开可调用的方法,我们借助源生成器的力量即可对其动态代理,接着,这些属性或方法将能被我们拦截

> 但因其本质是对 DispatchProxy 的封装,此功能仅在 .NET5 + 项目可用

```csharp
    public partial class Factory
    {
        [AspectOriented]
        public string UID { get; set; } = "default";

        [AspectOriented]
        public void Do()
        {
            
        }

        [AspectOriented]
        private int id = 3;

        // 也可以给字段标记，但这么做就必须实现对应的可读可写属性
        public int Id
        {
            get => id;
            set => id = value;
        }

        // 这里是一些编辑代理逻辑的示例，你在初始化类型后可对实例的Proxy进行编辑
        private void SetProxy()
        {
            Proxy.SetProxy(ProxyMembers.Setter, nameof(UID), // Setter 的 AOP
                null,
                (calls, result) =>
                {
                    var oldValue = UID;
                    var newValue = calls[0].ToString(); // 对于 Setter器，必定有一个参数 value
                    UID = newValue;
                    return Tuple.Create(oldValue, newValue); // 返回新值与旧值用于日志记录
                },
                (calls, result) =>
                {
                    var value = result as Tuple<string, string?>; // 接收上一个节点的返回值
                    MessageBox.Show($"值已更新 {value.Item1} → {value.Item2}"); // 编写日志
                    return null;
                });

            Proxy.SetProxy(ProxyMembers.Getter, nameof(Id), // Getter 的 AOP
                null,
                null,
                null);

            Proxy.SetProxy(ProxyMembers.Method,nameof(Do), // Method 的 AOP
                null,
                null,
                (calls, result) =>
                {
                    MessageBox.Show($"Do方法已执行过"); // 编写日志
                    return null;
                });
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Proxy.UID = "newUID"; // 通过代理设置 UID 属性
            Proxy.Do(); // 通过代理调用 Do 方法
        }
    }
```

## Ⅴ ThemeManager

> 极致简约的主题切换上下文维护，使用特性标记即可实现

> 首个泛型参数是一个值转换器，后面的泛型参数是主题类型，最多写入7个主题类型，每个主题使用一个object?[]传递构造参数

```csharp
    [ThemeConfig<ObjectConverter, Dark, Light>(nameof(Background), ["#1e1e1e"], ["#00ffff"])]
    [ThemeConfig<ObjectConverter, Dark, Light>(nameof(Foreground), ["#ffffff"], ["#1e1e1e"])]
    [ThemeConfig<ObjectConverter, Dark, Light>(nameof(Width), ["800"], ["400"])]
    public partial class MainWindow : Window
```

> 标记了特性后，类已经自动实现 IThemeObject 接口，你可使用下述方法来加载其主题功能

```csharp
    // [ 每个实例都需要 ] -> 初始化主题功能
    InitializeTheme();
    // [ 全局状态 ] -> 当前主题类型，只读，若要修改请在所有元素初始化主题前使用SetCurrent()修改
    ThemeManager.Current = typeof(Dark);
    // [ 全局选项 ] -> 主题切换时，初始值从 [ 反射 / 缓存 ] 获取
    ThemeManager.StartModel = StartModel.Reflect;
    // [ 全局仅执行一次 ] -> 设置平台插值器（ 由 VeloxDev.WPF / VeloxDev.MAUI … 等封装提供 ）
    ThemeManager.SetPlatformInterpolator(new Interpolator());
```

> 可以使用下述API来切换主题，注意需要特定平台的适配

```csharp
    // 渐变切换，支持缓动/FPS/持续时间参数，建议在界面元素少时使用
    ThemeManager.Transition<Light>(new TransitionEffect() { FPS = 60, Duration = TimeSpan.FromSeconds(3) });
    // 瞬时切换，适合界面元素较多时使用
    ThemeManager.Jump<Light>();
```

> 此外，生成内容还包括一些方法

```csharp
        // 主题切换的回调,可选择性地实现
        partial void OnThemeChanging(Type oldTheme, Type newTheme);
        partial void OnThemeChanged(Type oldTheme, Type newTheme);

        // 动态编辑主题资源值
        public void EditThemeValue<T>(string propertyName, object? newValue) where T : ITheme;
        public void RestoreThemeValue<T>(string propertyName) where T : ITheme;

        // 获取资源值字典
        public Dictionary<string, Dictionary<PropertyInfo, Dictionary<Type, object?>>> GetStaticCache();
        public Dictionary<string, Dictionary<PropertyInfo, Dictionary<Type, object?>>> GetActiveCache();
```

> ITheme 接口可用于自定义主题

```csharp
    public class Dark : ITheme
    {

    }
```

> IThemeValueConverter 接口可用于自定义主题转换器，例如，您希望从资源字典转换为主题资源，使其可以静态或动态地切换主题，那么自定义这个转换器是推荐的做法

```csharp
    public class ObjectConverter : IThemeValueConverter
    {
        public object? Convert(Type targetType, string propertyName, object?[] parameters)
        {
            // 实现转换逻辑
            return value;
        }
    }
```

## Ⅵ MonoBehaviour

> 自动创建、维护一个实时循环任务，可以修改其 FPS 以控制刷新频率，示例中的 MonoBehaviour 不传入参数代表使用默认的 60 FPS

```csharp
    [MonoBehaviour] // 默认 MonoBehaviour(60) 也就是 60 FPS
    public partial class MainWindow : Window
    {
        // 默认关闭,可以设置CanMonoBehaviour为true或false来开启或关闭 MonoBehaviour 功能
        
        partial void Start()
        {

        }
        partial void Update()
        {

        }
        partial void LateUpdate()
        {

        }
        partial void ExistMonoBehaviour()
        {
            
        }
    }
```