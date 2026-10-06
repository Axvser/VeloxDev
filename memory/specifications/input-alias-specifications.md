# 输入家族类型的别名规矩

> 用户口述、agent 落笔。约束**平台层引用 Core 输入事件家族那 15 个类型**时名字怎么写。
> **触发**：在 `Src/Adapters/`、`Src/Templates/`、`Examples/` 下写出 §二 那 15 个类型中的任何一个之前 —— 对照清单即可判断，不必先读代码。
> 本文是「这 15 个名字怎么写」的唯一事实源；机制背景与现行代码见 [WorkflowSystem/architecture.md §3.6](../modules/WorkflowSystem/architecture.md)。

---

## 一、为什么裸名不能用

适配器的命名空间是 `VeloxDev.WorkflowSystem.AttachedBehaviors` —— **Core 命名空间的下级**；而文件顶部那些 `using` 属于最外层的编译单元。C# 查名字是由最内层的命名空间声明**往外**找、`using` 最后才轮到，所以**外层命名空间里声明的同名类型静默胜出**：不报 CS0104 歧义，而是在下游报一串看着毫不相干的错。

2026-10-05 去掉 `Workflow` 前缀后第一次全量构建实测：**134 个错误、11 个文件** —— `CS1061`（这个类型不含 `GetPosition` / `Handled` / `Pointer`）、`CS0019`（运算符 `!=` 无法应用于 `MouseButton` 和 `MouseButton`）、`CS0115`（没有找到适合的方法来重写）。**没有一条指向真正的原因**，看的人会去翻平台文档。（这两个数字是当时量到的，此后工作区里复核不到 —— 现在复现只会看到「已全部消歧」，错误数为 0。）

⇒ 平台层这 15 个名字一律走别名，让「我用的是 Core 的」和「我用的是平台自己的」在源码上直接分得开，不靠解析规则推。

## 二、这 15 个类型

都在 `Src/Core/VeloxDev.Core/WorkflowSystem/GUI/Events/Input/`：

| 组 | 类型 |
|---|---|
| 能力接口 / 中转 | `IInputEvents`、`InputRelay` |
| 指针 | `PointerEventArgs` + `PointerEntered` / `PointerExited` / `PointerMoved` / `PointerPressed` / `PointerReleased` / `PointerWheel` 六个 args |
| 键盘 | `KeyEventArgs`、`KeyDownEventArgs`、`KeyUpEventArgs`、`InputKey` |
| 值类型 | `MouseButton`、`InputModifiers` |

**这 15 个之外的 Core 名字保持裸名**，别顺手也加前缀：

- `WorkflowInput`（入口）、`WorkflowEventHandle`、`Anchor`、`Size`、`IWorkflowTreeViewModel` 这一族；
- 按组件定制的事件族（`IWorkflowNodeEvents` / `IWorkflowSlotEvents` / `IWorkflowTreeEvents`）—— 它们的成员名不与平台类型同名，`Workflow` 前缀照留。

**新增一个输入家族类型时**：加进上表，并按 §三 用。清单与代码同源在 `GUI/Events/Input/`，改名 / 新增 / 删除后本文跟着改。

## 三、规矩

### 3.1 适配器与 item template：每一处引用都走别名，两个别名都声明

每个引用到这 15 个之一的文件，**同时声明两个别名**，并且**每一处引用都带前缀**：

```csharp
using Wf = VeloxDev.WorkflowSystem;          // Core 侧 → Wf.PointerPressedEventArgs
using PlatformInput = <平台输入命名空间>;      // 平台侧 → PlatformInput.PointerEventArgs
```

判据**不是**「这个名字在这个文件里冲不冲突」，而是「这一层不留裸名，读的人不必先推一遍解析规则」。**哪怕某个别名在本文件里一次都没用上，也留着** —— 靠省一行 `using` 换来的是一处特例，而特例正是这条规矩要消灭的东西。

`PlatformInput` 按平台取（与 [adapter-base-class-specifications.md §一](adapter-base-class-specifications.md) 的「无标记语言那一家的判据」无关，七家都要）：

| 平台 | `PlatformInput` |
|---|---|
| WPF | `System.Windows.Input` |
| Avalonia | `Avalonia.Input` |
| WinUI | `Windows.UI.Core` |
| MAUI | `Microsoft.Maui.Controls` |
| WinForms | `System.Windows.Forms` |
| Jalium | `Jalium.UI.Input` |
| Razor / Blazor | `Microsoft.AspNetCore.Components.Web` |

### 3.2 demo：不必，除非真的有冲突

`Examples/` 下的 demo 是「怎么写」的示范，不是必须消歧的地方 —— 保持裸名更短易读。**只在编译器真的报冲突的地方留别名**：把该文件的别名撤掉、构建一遍，看谁报 `CS0104`，报的那几处再按 §3.1 的纪律加回来。

⚠ **两个不在 CS0104 名单里的真冲突也别漏**（本轮踩过）：

- 一边写成**全限定**（`Avalonia.Input.KeyEventArgs`）—— 它本来就不进歧义名单，撤掉别名后才露出来；
- 一边**名字解析到了错的一侧**（`e.ChangedButton == MouseButton.X` 里的 `MouseButton` 是 Jalium 自己的枚举，被 Core 的静默盖住了）。

⇒ demo 层的自查**不是**「还有没有裸名」，而是**「构建还绿不绿」**。本轮 15 个 demo 文件里 11 个撤掉别名后干净、保持裸名，4 个要留：Avalonia `Views/Workflow/SlotView.axaml.cs`、Jalium `Views/Workflow/NodeEditorSurface.cs`、WPF `Views/Workflow/WorkflowView.xaml.cs`、WinForms `Controls/WorkflowCanvas.cs`。

### 3.3 判据按**用途**，不按名字

同一处名字，Core 侧用 `Wf.`、平台侧用 `PlatformInput.`：

| 这一处在干什么 | 用哪个 |
|---|---|
| `new X(...)` 造 Core 的 args、把 args 交给 `WorkflowInput.For(tree).Route(...)` | `Wf.` |
| 订 Core 中转（`events.Input.PointerEntered += …`） | `Wf.` |
| 让 Core 类型出现在字段 / 参数 / 返回值类型上（如 `IInputEvents? _input`） | `Wf.` |
| 平台事件处理器、`override` 的签名（`OnPointerPressed(object?, … e)` 的 `e`） | `PlatformInput.` |
| 在平台 args 上调平台成员（`e.GetPosition` / `e.Handled` / `e.Pointer` / `e.ChangedButton`） | `PlatformInput.` |
| 把平台按键码翻译成 Core 的（`Keys.Z` → `Wf.InputKey.Z`） | 左边平台、右边 `Wf.` |

**同一个方法里两种都可能有**，逐个出现判，不要按「这个文件是 Core 侧还是平台侧」一刀切：

- `ToButton(PlatformInput.MouseButton button)` 返回 `Wf.MouseButton`（WPF `WorkflowSurfaceBehavior.cs:836`）；
- `Modifiers(KeyModifiers keys)` 返回 `Wf.InputModifiers`（Avalonia `WorkflowSurfaceBehavior.cs:301`）。

⚠ 参数里的 `Key` / `Keys` / `KeyModifiers` / `VirtualKey` / `MouseButtons` **不在 15 个里**，保持裸名 —— 它们与 Core 的不同名（Core 是 `InputKey` / `InputModifiers` / `MouseButton`），不冲突。

## 四、核查

**别用 `grep -P` 加 `2>/dev/null` 自查**：这个环境默认 locale 下它报 `-P supports only unibyte and UTF-8 locales` 并**静默返回空**，会被读成「没有残留」（2026-10-05 一整轮的自查因此虚报为 0）。`git grep -P` 走自带的 PCRE，**在本机实测可用**，用它：

```bash
PAT='(?<![A-Za-z0-9_.])(IInputEvents|InputRelay|InputKey|InputModifiers|MouseButton|KeyDownEventArgs|KeyUpEventArgs|KeyEventArgs|PointerEnteredEventArgs|PointerExitedEventArgs|PointerMovedEventArgs|PointerPressedEventArgs|PointerReleasedEventArgs|PointerWheelEventArgs|PointerEventArgs)\b'

# 适配器与模板层：必须 0 行
git grep -nP --untracked "$PAT" -- 'Src/Adapters/*' 'Src/Templates/*'
```

**跑完必做正例**：拿同一串 `$PAT` 去扫一个已知有裸名的 demo 文件（如 `Examples/Workflow/Jalium Trimmed/Demo/Views/Workflow/LinkView.cs`），**得看到行才算这次扫描有效**。`--untracked` 不能省 —— 新建的输入家族文件还没 `git add` 时也要能被扫到。

命中的行若是**注释里的散文**，不算违规 —— 这条规矩管的是**代码里的引用**。当前模板的文档注释有 3 处这样的散文（`Src/Templates/*/working/content/workflow-link-view/TemplateClass.*`：`/// … subscribe <c>IInputEvents</c> on the helper`），是预期结果；散文里没必要写 `Wf.`。

⚠ **构建绿不是这条规矩的证据**：裸名解析到**正确**的命名空间时照样编译通过（本轮 6 个适配器文件残留裸名，构建一直是绿的）。这条只认扫描结果。

## 五、自查

改动平台层的输入代码后，提交前逐条过：

- [ ] 引用的是 §二 那 15 个之一吗？在不在 §二的「保持裸名」清单里？
- [ ] 适配器 / 模板层：本文件两个别名都在？每一处引用都带前缀（`Wf.` / `PlatformInput.`）？
- [ ] 前缀是按**用途**选的吗（Core 侧 `Wf.`、平台侧 `PlatformInput.`），而不是按名字？
- [ ] demo 层：构建还绿吗？撤掉别名后新露出的冲突处理了吗（含全限定与「解析到错的一侧」两种）？
- [ ] §四 的扫描跑了吗、正例通过了吗？
