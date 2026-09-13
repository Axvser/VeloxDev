# Transition 示例导读

这个目录里是 VeloxDev 插值动画系统（`VeloxDev.TransitionSystem`）的可运行示例：**同一套 API 在七个 GUI 上的 demo**，外加一个把它当真机验收跑的套件。

这里的每条链接都**钉在某个提交上**（`af4f7a0a`），不是分支。所以它们不会随代码漂移之后指向别处——读到的就是你点开时仓库里那一版。要对照当前代码，把链接里的 SHA 换成 `master` 即可。

---

## 先跑起来

```bash
dotnet build "Examples/Transition/WPF/Demo/Demo.csproj" -c Debug
dotnet build "Examples/Transition/Blazor/Demo/Demo/Demo.csproj" -c Debug
```

桌面那六个平台各有自己的 `Demo.csproj`，Blazor 多一层目录。跑起来后，每一行用例都是一条**真的 Transition**：点「启动」就跑，屏幕上动的是真控件/真对象。

---

## 七个平台

| 平台 | demo | 演示的目标是什么 |
|---|---|---|
| WPF | [WPF/Demo](https://github.com/Axvser/VeloxDev/tree/af4f7a0a0fda7600ed527317c7f773762ea1579c/Examples/Transition/WPF/Demo) | `Rectangle` 等真控件 |
| Avalonia | [Avalonia/Demo](https://github.com/Axvser/VeloxDev/tree/af4f7a0a0fda7600ed527317c7f773762ea1579c/Examples/Transition/Avalonia/Demo) | 同上 |
| WinUI | [WinUI/Demo](https://github.com/Axvser/VeloxDev/tree/af4f7a0a0fda7600ed527317c7f773762ea1579c/Examples/Transition/WinUI/Demo) | 同上 |
| MAUI | [MAUI/Demo](https://github.com/Axvser/VeloxDev/tree/af4f7a0a0fda7600ed527317c7f773762ea1579c/Examples/Transition/MAUI/Demo) | 同上 |
| WinForms | [WinForms/Demo](https://github.com/Axvser/VeloxDev/tree/af4f7a0a0fda7600ed527317c7f773762ea1579c/Examples/Transition/WinForms/Demo) | 同上 |
| Jalium | [Jalium/Demo](https://github.com/Axvser/VeloxDev/tree/af4f7a0a0fda7600ed527317c7f773762ea1579c/Examples/Transition/Jalium/Demo) | 同上 |
| Blazor | [Blazor/Demo](https://github.com/Axvser/VeloxDev/tree/af4f7a0a0fda7600ed527317c7f773762ea1579c/Examples/Transition/Blazor/Demo) | 一个普通 C# 对象（浏览器里没有控件属性可写） |

**API 完全一致**：七个 demo 里那条动画长得一模一样，不同的只有目标对象与适配器包。下面的例子大多指向 WPF 版，其余六个平台在同一位置有同构的写法。

---

## 一条动画长什么样

[`WPF/Demo/MainWindow.xaml.cs:900-910`](https://github.com/Axvser/VeloxDev/blob/af4f7a0a0fda7600ed527317c7f773762ea1579c/Examples/Transition/WPF/Demo/MainWindow.xaml.cs#L900-L910)

```csharp
private static readonly Transition<Rectangle> Animation0 =
    Transition<Rectangle>.Create()
        .Property(r => r.Opacity, 0)
        .Property(r => ((TranslateTransform)r.RenderTransform).X, LoadTravel)
        .Property(r => r.Fill, new SolidColorBrush(Colors.Orange))
        .Effect(new TransitionEffect()
        {
            Duration = TimeSpan.FromSeconds(2),
            IsAutoReverse = true,
            LoopTime = 2,
        });
```

声明是**不可变的**，可以 `static readonly` 缓存；`Execute(target)` 才是启动（[`:720`](https://github.com/Axvser/VeloxDev/blob/af4f7a0a0fda7600ed527317c7f773762ea1579c/Examples/Transition/WPF/Demo/MainWindow.xaml.cs#L720)）。同一条声明可以对不同 target 反复执行。

---

## 属性路径

系统读的是路径**末段**的当前值与目标值，中间每一段只是导航。

**单属性** — [`MainWindow.xaml.cs:902`](https://github.com/Axvser/VeloxDev/blob/af4f7a0a0fda7600ed527317c7f773762ea1579c/Examples/Transition/WPF/Demo/MainWindow.xaml.cs#L902)

**嵌套路径**（子对象上的成员，且是被就地改的那一个）— [`:903`](https://github.com/Axvser/VeloxDev/blob/af4f7a0a0fda7600ed527317c7f773762ea1579c/Examples/Transition/WPF/Demo/MainWindow.xaml.cs#L903)

```csharp
.Property(r => ((TranslateTransform)r.RenderTransform).X, LoadTravel)
```

**缓存型端点是工厂** — 引用类型的端点若跨帧复用，一个就地改写的采样器会把后面的帧一起带偏：

- [`:916`](https://github.com/Axvser/VeloxDev/blob/af4f7a0a0fda7600ed527317c7f773762ea1579c/Examples/Transition/WPF/Demo/MainWindow.xaml.cs#L916) 整个变换对象被换掉，逐字段外推，并指定旋转方向
- [`:925-931`](https://github.com/Axvser/VeloxDev/blob/af4f7a0a0fda7600ed527317c7f773762ea1579c/Examples/Transition/WPF/Demo/MainWindow.xaml.cs#L925-L931) 两个变换合成一个 `TransformGroup`，按类型配对插值

**索引器路径**（下标是路径身份的一部分）— [`WPF/Demo/SamplerProbe.cs:163-172`](https://github.com/Axvser/VeloxDev/blob/af4f7a0a0fda7600ed527317c7f773762ea1579c/Examples/Transition/WPF/Demo/SamplerProbe.cs#L163-L172)

```csharp
() => GradientStopPath(0),   // Ramp.GradientStops[0].Color
```

路径由表达式建出来，见 [`:182-188`](https://github.com/Axvser/VeloxDev/blob/af4f7a0a0fda7600ed527317c7f773762ea1579c/Examples/Transition/WPF/Demo/SamplerProbe.cs#L182-L188)。数组下标同理 — [`Blazor/Demo/.../SamplerProbe.cs:44`](https://github.com/Axvser/VeloxDev/blob/af4f7a0a0fda7600ed527317c7f773762ea1579c/Examples/Transition/Blazor/Demo/Demo/Models/SamplerProbe.cs#L44) 与 [`:84-89`](https://github.com/Axvser/VeloxDev/blob/af4f7a0a0fda7600ed527317c7f773762ea1579c/Examples/Transition/Blazor/Demo/Demo/Models/SamplerProbe.cs#L84-L89)。

> **下标是身份的一部分。** `Items[0].Opacity` 与 `Items[1].Opacity` 是两条不同的路径，不会互相覆盖。这是承重行为，不是实现细节。

**把索引钉住**：默认索引每帧重新求值（路径跟着索引走）；要它在动画启动时读一次、整程锚在同一个槽上，用 `PathIndex.Frozen`：

[`Src/Core/VeloxDev.Core/TransitionSystem/PathIndex.cs:27-34`](https://github.com/Axvser/VeloxDev/blob/af4f7a0a0fda7600ed527317c7f773762ea1579c/Src/Core/VeloxDev.Core/TransitionSystem/PathIndex.cs#L27-L34)

```csharp
.Property(x => x.Items[PathIndex.Frozen(i)].Width, 120)
```

---

## 时间参数与缓动

一条 effect 上能调的都在 [`TransitionEffect`](https://github.com/Axvser/VeloxDev/blob/af4f7a0a0fda7600ed527317c7f773762ea1579c/Src/Core/VeloxDev.Core/TransitionSystem/TransitionEffect.cs)：

```csharp
.Effect(new TransitionEffect()
{
    Duration = TimeSpan.FromSeconds(2),
    IsAutoReverse = true,   // 到终点后再跑回来
    LoopTime = 2,           // 整段重复几次
    FPS = 144,              // 采样率上限，不是帧栅格
    Ease = Eases.Circ.InOut,
})
```

- 全部参数写满的一份：[`MainWindow.xaml.cs:932-939`](https://github.com/Axvser/VeloxDev/blob/af4f7a0a0fda7600ed527317c7f773762ea1579c/Examples/Transition/WPF/Demo/MainWindow.xaml.cs#L932-L939)
- 缓动目录（9 族 × In/Out/InOut）：[`Src/Core/.../Eases.cs`](https://github.com/Axvser/VeloxDev/blob/af4f7a0a0fda7600ed527317c7f773762ea1579c/Src/Core/VeloxDev.Core/TransitionSystem/Eases.cs)

**过冲不被钳制**，这是有意的：`Back` / `Elastic` 越过目标再回来，`Eases` 里那些曲线才名副其实。

---

## 分段：等待与串联

`.Await` 等一段时间再开始，`.AwaitThen` 在**上一段跑完之后**再等一段时间开始下一段：

- `.Await` — [`MainWindow.xaml.cs:915`](https://github.com/Axvser/VeloxDev/blob/af4f7a0a0fda7600ed527317c7f773762ea1579c/Examples/Transition/WPF/Demo/MainWindow.xaml.cs#L915)
- `.AwaitThen` — [`:940`](https://github.com/Axvser/VeloxDev/blob/af4f7a0a0fda7600ed527317c7f773762ea1579c/Examples/Transition/WPF/Demo/MainWindow.xaml.cs#L940)（后面再挂一段 `.Property` + `.Effect`，就成了"先动、停 5 秒、再动另一处"）
- 另一条同构的：Avalonia [`MainWindow.axaml.cs:1016-1018`](https://github.com/Axvser/VeloxDev/blob/af4f7a0a0fda7600ed527317c7f773762ea1579c/Examples/Transition/Avalonia/Demo/Views/MainWindow.axaml.cs#L1016-L1018)

链上每一段都是一条独立的时间线，段与段之间那段等待**也会被暂停**，不会白白流走。

---

## 换采样器

默认按属性类型从注册表取采样器；要对某一条属性换一个，用 `SetInterpolator`：

[`MainWindow.xaml.cs:344-356`](https://github.com/Axvser/VeloxDev/blob/af4f7a0a0fda7600ed527317c7f773762ea1579c/Examples/Transition/WPF/Demo/MainWindow.xaml.cs#L344-L356)

```csharp
var animation = Transition<SamplerSubject>.Create()
    .Effect(new TransitionEffect { Duration = SamplerProbe.BenchDuration, Ease = Eases.Back.Out });

animation.GetState().SetValue(property, SamplerProbe.End(sampler));
animation.GetState().SetInterpolator(property, SamplerProbe.Create(sampler));
animation.Execute(subject);
```

自定义采样器实现 `ISampler`：三个方法，**无状态**，注册表里的实例是全进程共用的。

---

## 结构体整值装配

一个没有注册采样器的**结构体**属性，可以让它的成员各自插值、每帧用构造函数把整值拼回来：实现 `ISampleable`。

[`AUTO TEST/Samplers/CoreSamplerEntries.cs:36-53`](https://github.com/Axvser/VeloxDev/blob/af4f7a0a0fda7600ed527317c7f773762ea1579c/Examples/Transition/AUTO%20TEST/Samplers/CoreSamplerEntries.cs#L36-L53)

```csharp
public IReadOnlyList<ITransitionProperty> GetAnimatableMembers()
    => TransitionProperty.ReadableMembers<SampleablePair>(pair => pair.A, pair => pair.B);

public object? CreateFrameValue(IReadOnlyList<object?> memberValues)
    => new SampleablePair((double)memberValues[0]!, (int)memberValues[1]!);
```

装配走编译期构造函数调用，没有反射。仓库里真实用到它的是 [`Viewport`](https://github.com/Axvser/VeloxDev/blob/af4f7a0a0fda7600ed527317c7f773762ea1579c/Src/Core/VeloxDev.Core/WorkflowSystem/GUI/GeometryModels/Viewport.cs)。

---

## 启动：互斥还是并发

`Execute(target, CanMutualTask:)` 决定这条动画与**同一个 target** 上已有动画的关系：

[`MainWindow.xaml.cs:716-762`](https://github.com/Axvser/VeloxDev/blob/af4f7a0a0fda7600ed527317c7f773762ea1579c/Examples/Transition/WPF/Demo/MainWindow.xaml.cs#L716-L762)

| 写法 | 行为 |
|---|---|
| `.Execute(target)` | **互斥**（默认）：新动画顶掉同 target 上正在跑的 |
| `.Execute(target, CanMutualTask: false)` | **并发**：互不取消，各跑各的 |
| `Task.Run(() => …Execute(target))` | 从非 UI 线程启动，一样合法 |

`.Execute` 立刻返回（`async void`）；要等它跑完，用 effect 的 `Completed` 事件或读目标的真实属性。

---

## 运行期控制时间轴

整套控制都按 **target** 寻址，不挂在快照上：

[`MainWindow.xaml.cs:771-816`](https://github.com/Axvser/VeloxDev/blob/af4f7a0a0fda7600ed527317c7f773762ea1579c/Examples/Transition/WPF/Demo/MainWindow.xaml.cs#L771-L816)

```csharp
Transition.Pause(target, IncludeMutual: true, IncludeNoMutual: true);
Transition.Resume(target, IncludeMutual: true, IncludeNoMutual: true);
Transition.SetRate(target, 0.25d, IncludeMutual: true, IncludeNoMutual: true);
Transition.Seek(target, Transition.Cycle(target, mutual, noMutual) + 1, TimeSpan.Zero, mutual, noMutual);
```

- **暂停不计入时长**：暂停多久，剩下的时长就还是那么多，不是"跳过去"。
- **暂停期间零定时器唤醒**：采样循环停在信号上，不是空转。
- **变速不跳帧**：速率改变前会先重设基准，播放中调速率是连续的。
- **时间只往前走**：没有反向播放；要回到某处用 `Seek`。负速率会被拒绝而不是钳制。

读回当前状态（暂停与否、速率、位置、第几程）— [`MainWindow.xaml.cs:654-661`](https://github.com/Axvser/VeloxDev/blob/af4f7a0a0fda7600ed527317c7f773762ea1579c/Examples/Transition/WPF/Demo/MainWindow.xaml.cs#L654-L661)

---

## 终止

[`MainWindow.xaml.cs:818-825`](https://github.com/Axvser/VeloxDev/blob/af4f7a0a0fda7600ed527317c7f773762ea1579c/Examples/Transition/WPF/Demo/MainWindow.xaml.cs#L818-L825)

```csharp
Transition.Exit(target, IncludeMutual: true, IncludeNoMutual: true);
```

**原地冻结**，不是跳到终点。要让目标回到声明的起点，得自己把起点写回去 —— 这正是每个 demo 里「重置」按钮做的事，见 [`WPF/Demo/MainWindow.xaml.cs:215`](https://github.com/Axvser/VeloxDev/blob/af4f7a0a0fda7600ed527317c7f773762ea1579c/Examples/Transition/WPF/Demo/MainWindow.xaml.cs#L215) 附近。

---

## 一次性启动一批

多行同时跑，用一次点击驱动一整张表：每行是一条真 Transition，全部启动就是一次并发压力场景。

[`MainWindow.xaml.cs:372`](https://github.com/Axvser/VeloxDev/blob/af4f7a0a0fda7600ed527317c7f773762ea1579c/Examples/Transition/WPF/Demo/MainWindow.xaml.cs#L372) 与它驱动的行表 [`:120-152`](https://github.com/Axvser/VeloxDev/blob/af4f7a0a0fda7600ed527317c7f773762ea1579c/Examples/Transition/WPF/Demo/MainWindow.xaml.cs#L120-L152)。

> **每一行要有自己的目标。** 一批动画共用一个对象时，互斥语义会让后一条顶掉前一条 —— 屏幕上看起来像"前几行没跑"。七个平台里每行各自一个目标，正是这个原因。

---

## 验收：这些示例是被真机跑过的

`AUTO TEST/` 是把上面这些 demo **当真的 app 启动、用 UI Automation 驱动**的验收套件：一次点击同时验证闭式解算术与"真动画在真控件上落到的值"，覆盖全部七个平台。

它的操作手册（怎么跑、验什么、怎么加一个平台）在 [AUTO TEST/AGENTS.md](https://github.com/Axvser/VeloxDev/blob/af4f7a0a0fda7600ed527317c7f773762ea1579c/Examples/Transition/AUTO%20TEST/AGENTS.md)。
