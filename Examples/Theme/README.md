# VeloxDev Dynamic Theme System  

**编译时生成 · 无反射开销 · 支持动画过渡 · 多主题热切换**

---

## ✍️ 1. 代码怎么写？

### 步骤 1：定义主题类型（实现 `ITheme`）
```csharp
public class Light : ITheme { }
public class Dark : ITheme { }
// 这两个是自带的，可扩展更多主题：BlueTheme, HighContrast 等
```

### 步骤 2：在 Window/UserControl 上配置属性映射
```csharp
[ThemeConfig<BrushConverter, Light, Dark>(
    nameof(Background), 
    ["#ffffff"],   // Light 主题值
    ["#1e1e1e"]    // Dark 主题值
)]
[ThemeConfig<BrushConverter, Light, Dark>(
    nameof(Foreground),
    ["#1e1e1e"],
    ["#ffffff"]
)]
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        LoadTheme(); // 必须调用
    }

    private void LoadTheme()
    {
        InitializeTheme(); // ← 自动生成，必须晚于 InitializeComponent()
        ThemeManager.SetPlatformInterpolator(new Interpolator()); // 启用动画
        ThemeManager.StartModel = StartModel.Cache; // 动画起始值来源
    }
}
```
> ✅ 每个 `[ThemeConfig]` 绑定一个属性到多个主题的值。  
> ✅ 支持任意数量属性和最多 7 个主题。

---

### 步骤 3：切换主题
```csharp
// 带渐变动画切换
ThemeManager.Transition<Dark>(TransitionEffects.Theme);

// 无动画瞬切
ThemeManager.Jump<Light>();

// 监听切换事件（可选）
partial void OnThemeChanged(Type? oldValue, Type? newValue)
{
    // 如弹出提示、保存设置等
}
```

### 步骤 4：动态修改主题值（运行时覆盖）
```csharp
// 临时修改 Light 主题的 Background
EditThemeValue<Light>(nameof(Background), new object?[] { "#ff0000" });

// 恢复默认值
RestoreThemeValue<Light>(nameof(Background));
```

---

## 📚 2. 核心 API 列表

### 特性：`[ThemeConfig<TConverter, T1, T2, ...>]`
| 参数 | 说明 |
|------|------|
| `TConverter` | 值转换器（如 `BrushConverter`） |
| `T1..T7` | 主题类型（必须实现 `ITheme`） |
| `string propertyName` | 要绑定的属性名（如 `"Background"`） |
| `object?[] context1..7` | 各主题的构造参数（传递给 Converter） |

### 全局管理器：`ThemeManager`
| 方法/属性 | 说明 |
|----------|------|
| `SetPlatformInterpolator(interpolator)` | 设置平台插值器（启用动画必需） |
| `StartModel` | 动画起始值来源：<br>`StartModel.Cache`（默认）或 `Reflect` |
| `Transition<T>(effect)` | 带动画切换到主题 T |
| `Jump<T>()` | 无动画切换到主题 T |
| `Current` | 当前主题类型（`typeof(Dark)`） |

### 自动生成的方法（在标记类中可用）
| 方法 | 说明 |
|------|------|
| `InitializeTheme()` | 初始化主题（必须调用） |
| `EditThemeValue<T>(prop, values)` | 动态覆盖主题值 |
| `RestoreThemeValue<T>(prop)` | 恢复默认值 |
| `GetStaticCache()` | 获取编译时静态资源 |
| `GetActiveCache()` | 获取运行时动态覆盖资源 |

### 部分方法（可选重写）
| 方法 | 触发时机 |
|------|--------|
| `OnThemeChanging(old, new)` | 切换前 |
| `OnThemeChanged(old, new)` | 切换后 |

---

> 💡 **一句话使用**：  
> **用 `[ThemeConfig]` 声明属性与主题值 → 调用 `InitializeTheme()` → 通过 `Transition<T>()` 或 `Jump<T>()` 切换主题。**