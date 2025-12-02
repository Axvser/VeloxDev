# VeloxDev AOT Reflection Generator  

**编译时生成反射调用代码以在 AOT 保留反射上下文**

---

## ✍️ 1. 代码怎么写？

### 步骤 1：标记需要反射的类型
```csharp
[AOTReflection(
    Namespace = "MyApp.Models", // 可选：指定生成类的命名空间
    Constructors = true,
    Methods = false,
    Properties = true,
    Fields = false
)]
public class MyViewModel
{
    public string Name { get; set; }
    public MyViewModel(string name) => Name = name;
}
```
> ✅ 仅需在 **class 或 struct** 上添加 `[AOTReflection]`。  
> ✅ 所有参数均为可选，默认全开启（`Constructors=true, Methods=true...`）。

---

### 步骤 2：使用反射（无需额外操作）
```csharp
// 在  AOT 环境中安全使用
var type = typeof(MyViewModel);
var ctor = type.GetConstructor(new[] { typeof(string) }); // ✅ 可用
var prop = type.GetProperty("Name");                      // ✅ 可用
```
> ✅ 编译时自动生成 `MyApp.Models.AOTReflection.g.cs`，调用 `typeof(T).GetTypeInfo()` 确保元数据保留。  

---

## 📚 2. 核心 API 列表

### 特性：`[AOTReflection]`
| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Namespace` | `string` | `"Auto"` | 生成类的命名空间：<br>`"Auto"` = 自动推导最长公共前缀 |
| `Constructors` | `bool` | `true` | 是否保留构造函数元数据 |
| `Methods` | `bool` | `true` | 是否保留方法元数据 |
| `Properties` | `bool` | `true` | 是否保留属性元数据 |
| `Fields` | `bool` | `true` | 是否保留字段元数据 |

### 生成内容（自动创建）
- 文件名：`{Namespace}.AOTReflection.g.cs`
- 内容：
  ```csharp
  namespace MyApp.Models
  {
      public static class AOTReflection
      {
          public static void Init()
          {
              _ = typeof(MyViewModel).GetTypeInfo();
              _ = typeof(MyViewModel).GetConstructors(...);
              _ = typeof(MyViewModel).GetProperties(...);
          }
      }
  }
  ```
> ✅ 调用 `Init()` 即可确保所有标记类型的反射信息被 AOT 编译器保留。

---

> 💡 **一句话使用**：  
> **给类型加 `[AOTReflection]` → 编译时自动生成反射元数据 → 在 AOT 中安全使用 `typeof(T).GetMethod/GetProperty` 等。**