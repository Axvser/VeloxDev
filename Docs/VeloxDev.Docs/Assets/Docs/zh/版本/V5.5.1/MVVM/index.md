# MVVM 生成器多框架适配

> **涉及文件：**
>
> - `Src/Generators/VeloxDev.Core.Generator/Base/Analizer.cs`（+97 / -15 行）
> - `Src/Generators/VeloxDev.Core.Generator/Writers/MVVMWriter.cs`（+65 / -6 行）
>   **变更量：** +162 / -21 行

---

## 背景

此前源码生成器对所有类型使用统一的 setter 模式：

```csharp
if(Object.Equals(_field, value)) return;
var old = _field;
OnPropertyChanging(...);
_field = value;
OnPropertyChanged(new PropertyChangedEventArgs(nameof(Prop)));
```

这在无 MVVM 基类时工作良好，但当类继承自 `ObservableObject`、`BindableBase`、`ReactiveObject` 等框架基类时，会**重复触发属性通知**，且无法利用框架内置的 `SetProperty` 等优化机制。

## 改进设计

### SetterMode 枚举

新增在 `Analizer.cs` 中：

```csharp
public enum SetterMode
{
    /// <summary>直接字段赋值 + Object.Equals 检查（v5.4.0 原有行为）</summary>
    Default,

    /// <summary>委托到 SetProperty<T>(ref T, T, string)</summary>
    FrameworkSetProperty,

    /// <summary>委托到 this.RaiseAndSetIfChanged<T>(ref T, T, string) [ReactiveUI]</summary>
    FrameworkRaiseAndSet,

    /// <summary>使用 NotifyOfPropertyChange(string) [Caliburn.Micro]</summary>
    FrameworkNotifyOfPropertyChange,
}
```

### 自动检测流程

```mermaid
flowchart TD
    A[DetectSetterMode 检测] --> B{[ObservableObject] 属性?}
    B -->|是| C[FrameworkSetProperty<br>← CommunityToolkit.Mvvm]

    B -->|否| D{基类 BindableBase<br>或 SetProperty(ref T,T,string)?}
    D -->|是| E[FrameworkSetProperty<br>← Prism]

    D -->|否| F{实现 IReactiveObject?}
    F -->|是| G[FrameworkRaiseAndSet<br>← ReactiveUI]

    F -->|否| H{有 NotifyOfPropertyChange?}
    H -->|是| I[FrameworkNotifyOfPropertyChange<br>← Caliburn.Micro]

    H -->|否| J[Default<br>← 无 MVVM 基类]
```

### 各框架生成的 Setter 对比

#### Default（无 MVVM 基类）

```csharp
set
{
    if (global::System.Object.Equals(_field, value)) return;
    var old = _field;
    OnPropertyChanging(nameof(Field));
    OnFieldChanging(old, value);
    _field = value;
    OnFieldChanged(old, value);
    OnPropertyChanged(new PropertyChangedEventArgs(nameof(Field)));
}
```

#### FrameworkSetProperty（CommunityToolkit.Mvvm / Prism）

```csharp
set
{
    var old = _field;
    OnPropertyChanging(nameof(Field));
    OnFieldChanging(old, value);
    if (SetProperty(ref _field, value, nameof(Field)))
    {
        OnFieldChanged(old, _field);
        // SetProperty 内部已触发 PropertyChanged，不重复调用 OnPropertyChanged
    }
}
```

#### FrameworkRaiseAndSet（ReactiveUI）

```csharp
set
{
    var old = _field;
    OnPropertyChanging(nameof(Field));
    this.RaiseAndSetIfChanged(ref _field, value, nameof(Field));
    OnFieldChanged(old, _field);
    OnPropertyChanged(...);
}
```

#### FrameworkNotifyOfPropertyChange（Caliburn.Micro）

```csharp
set
{
    if (global::System.Object.Equals(_field, value)) return;
    var old = _field;
    OnPropertyChanging(nameof(Field));
    OnFieldChanging(old, value);
    _field = value;
    NotifyOfPropertyChange(nameof(Field));  // 尊重 IsNotifying 检查
    OnFieldChanged(old, value);
    OnPropertyChanged(...);
}
```

---

## 通知方法查找优先级调整

`MVVMWriter.cs` 中的 `BuildNotifyMethodInvocation` 方法优先级改为：

1. **优先查找框架特定的转发方法名**（`RaisePropertyChanged`、`NotifyOfPropertyChange` 等）
2. **其次才是 `EventArgs` 重载**（`OnPropertyChanged(PropertyChangedEventArgs)`）
3. **最后是 ReactiveUI 扩展方法**

> **修复的问题：** Caliburn.Micro 此前优先匹配 `OnPropertyChanged(PropertyChangedEventArgs)` 导致跳过了 `NotifyOfPropertyChange`（后者包含 `IsNotifying` 检查），现在优先使用 `NotifyOfPropertyChange`。

---

## 各框架已知成员重叠

| 框架                  | 重叠事件                                  | 重叠方法                                                                                                                       | 需生成的事件/方法                                                                      |
| --------------------- | ----------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------ | -------------------------------------------------------------------------------------- |
| CommunityToolkit.Mvvm | `PropertyChanging`, `PropertyChanged` | `OnPropertyChanging/Changed(string)` (protected, non-virtual); `OnPropertyChanging/Changed(EventArgs)` (protected virtual) | 无（全由基类提供）                                                                     |
| Prism BindableBase    | `PropertyChanged` 仅                    | `OnPropertyChanged(EventArgs)` (protected virtual); `RaisePropertyChanged(string)` (protected, non-virtual)                | `PropertyChanging` 事件, `INotifyPropertyChanging`, `OnPropertyChanging(string)` |
| ReactiveUI            | `PropertyChanging`, `PropertyChanged` | 无（显式接口实现 + 扩展方法）                                                                                                  | `OnPropertyChanging(string)` → 委托到扩展方法                                       |
| Caliburn.Micro        | `PropertyChanged` 仅                    | `OnPropertyChanged(EventArgs)` (protected); `NotifyOfPropertyChange(string)` (public virtual)                              | `PropertyChanging` 事件, `INotifyPropertyChanging`, `OnPropertyChanging(string)` |

---

## 代码质量提升

- 将原来内联在 `GenerateViewModel()` 中的 setter 逻辑提取为 `GetSetterBodyLines()` 方法，按 `SetterMode` 分发
- 添加 `FrameworkSetterMode` 属性到 `MVVMPropertyFactory`
- 通过 `FrameworkSetProperty` 模式排除 `SetteredBody`，避免重复通知