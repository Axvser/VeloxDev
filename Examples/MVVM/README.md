# VeloxDev MVVM & Command System  

[← 返回主页](../../README.md)

**编译时生成属性通知 · 声明式异步命令 · 内置并发控制与生命周期事件**

> 💡 本文档覆盖 **快速上手 → 完整 API**，可直接对照 Demo 源码（`Examples/MVVM/` 下各平台子目录）阅读。

---

## ✍️ 1. 代码怎么写？

### 步骤 1：定义可通知属性（字段标记）
```csharp
public partial class MyViewModel
{
    [VeloxProperty] private string _name = "Default";
    [VeloxProperty] private int _count;
    // 自动生成 Name / Count 属性 + INotifyPropertyChanged
}
```
> ✅ 仅需在 **私有字段** 上加 `[VeloxProperty]`。  
> ✅ 编译时自动生成 `public T Property { get; set; }` 及 `OnPropertyChanging/Changed` 调用。

---

### 步骤 2：定义异步命令（方法标记）
```csharp
public partial class MyViewModel
{
    // 基础命令（无验证，允许1个并发）
    [VeloxCommand]
    private async Task SaveAsync(object? parameter, CancellationToken ct)
    {
        await File.WriteAllTextAsync("data.txt", _name, ct);
    }

    // 高级命令（带执行验证 + 并发控制）
    [VeloxCommand(name: "Submit", canValidate: true, semaphore: 3)]
    private async Task SubmitAsync(object? parameter, CancellationToken ct)
    {
        // 执行逻辑
    }

    // 必须实现：验证方法（当 canValidate=true 时）
    private partial bool CanExecuteSubmitCommand(object? parameter)
    {
        return !string.IsNullOrEmpty(_name) && _count > 0;
    }
}
```
> ✅ 方法签名必须为：`Task MethodName(object? parameter, CancellationToken ct)`。  
> ✅ `semaphore` 控制最大并发数（默认 1）。

---

### 步骤 3：在 View 中绑定
```xml
<!-- XAML -->
<Button Command="{Binding SaveCommand}" />
<Button Command="{Binding SubmitCommand}" IsEnabled="{Binding SubmitCommand.CanExecute}" />
```
```csharp
// Code-behind 或测试
viewModel.SaveCommand.Execute(null);
```

---

## 📚 2. 核心 API 列表

### 特性：`[VeloxProperty]`
| 作用 | 说明 |
|------|------|
| 标记字段 | 自动生成 INPC 属性 |
| 位置 | 仅限 **private field** |
| 生成内容 | `public T Property { get; set; }` + `OnPropertyChanging/OnPropertyChanged` |

### 集合属性的特殊行为
如果 `VeloxProperty` 对应的类型实现了 `INotifyCollectionChanged`（例如 `ObservableCollection<T>`），生成器会将其视为**集合通知属性**，而不是普通属性：

- 在 setter 中自动取消旧集合订阅并订阅新集合的 `CollectionChanged`
- 在集合实例替换时调用：
  - `OnItemRemovedFrom{Property}(IEnumerable<T>)`
  - `OnItemAddedTo{Property}(IEnumerable<T>)`
- 在集合内容变化时自动分发：
  - `Add` → `OnItemAddedTo{Property}(IEnumerable<T>)`
  - `Remove` → `OnItemRemovedFrom{Property}(IEnumerable<T>)`
  - `Replace` → 先移除再添加
  - `Move` → `OnItemMovedIn{Property}(IEnumerable<T>)`
  - `Reset` → `OnItemsResetIn{Property}()`

同时会生成一个可复写的基础钩子：

```csharp
protected virtual void OnCollectionChanged<T>(
    string propertyName,
    NotifyCollectionChangedEventArgs e,
    IEnumerable<T>? oldItems,
    IEnumerable<T>? newItems)
{
}
```

如果基类已经存在该方法，派生类不会重复生成这部分基础设施。

示例：

```csharp
public partial class UserListViewModel
{
    public UserListViewModel()
    {
        // 请通过属性初始化数据，以确保集合通知机制正常工作
        Users = new ObservableCollection<User>();
    }

    [VeloxProperty]
    private ObservableCollection<User> _users;

    partial void OnItemAddedToUsers(IEnumerable<User> items)
    {
        foreach (var item in items)
        {
            // 新增项处理
        }
    }

    partial void OnItemRemovedFromUsers(IEnumerable<User> items)
    {
        foreach (var item in items)
        {
            // 移除项处理
        }
    }

    partial void OnItemsResetInUsers()
    {
        // Clear / Reset 处理
    }

    protected override void OnCollectionChanged<T>(
        string propertyName,
        NotifyCollectionChangedEventArgs e,
        IEnumerable<T>? oldItems,
        IEnumerable<T>? newItems)
    {
        // 所有集合属性的统一入口
    }
}
```

### 特性：`[VeloxCommand]`
| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `name` | `string` | `"Auto"` | 命令属性名（`"Auto"` = 方法名） |
| `canValidate` | `bool` | `false` | 是否启用 `CanExecute` 验证 |
| `semaphore` | `int` | `1` | 最大并发执行数 |

### 自动生成的命令属性
| 类型 | 名称 | 说明 |
|------|------|------|
| `IVeloxCommand` | `{Name}Command` | 如 `SaveCommand`, `SubmitCommand` |

### `IVeloxCommand` 扩展能力
| 方法/事件 | 说明 |
|----------|------|
| `Task ExecuteAsync(object? param)` | 异步执行命令 |
| `event VeloxCommandEventHandler TaskStarted/Completed/Failed...` | 全生命周期事件 |
| `Lock()` / `UnLock()` | 手动暂停/恢复命令 |
| `Interrupt()` / `Clear()` | 取消正在执行或排队的任务 |
| `ChangeSemaphore(int n)` | 动态调整并发数 |

### 必须实现的验证方法（当 `canValidate=true`）
```csharp
private partial bool CanExecute{CommandName}Command(object? parameter);
// 例如：CanExecuteSubmitCommand
```

---

> 💡 **一句话使用**：  
> **字段加 `[VeloxProperty]` → 方法加 `[VeloxCommand]` → 编译时自动生成完整 MVVM 属性与命令，支持并发、验证、生命周期监控。**