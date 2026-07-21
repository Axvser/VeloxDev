# 项模板

VeloxDev 提供 **7 个 VS 项模板**，一键生成工作流各组件的视图代码。

---

## 安装

```shell
dotnet new install VeloxDev.WPF.Templates
```

## 使用

`-n` 指定名称，`-o` 指定输出目录：

```shell
# 创建节点视图
dotnet new wpf-v-node -n MyCustomNode -o Views

# 创建连接视图
dotnet new wpf-v-link -n MyLinkView -o Views

# 创建网格装饰器
dotnet new wpf-v-decorator -n MyGridDecorator -o Views

# 创建 Slot 视图
dotnet new wpf-v-slot -n MySlotView -o Views
```

## 模板清单

| 短名称 | 类型 | 说明 |
|--------|------|------|
| `wpf-v-tree` | Tree View | 画布容器 |
| `wpf-v-node` | Node View | 节点卡片 |
| `wpf-v-slot` | Slot View | 连接端点 |
| `wpf-v-link` | Link View | 折线连接 |
| `wpf-v-decorator` | Grid Decorator | 网格标尺 |
| `wpf-v-overlay` | Minimap Overlay | 小地图 |
| `wpf-v-selector` | Template Selector | 类型选择器 |

更详细的参数表见 [深入解析 → 项模板参数](xref:2_原理解析/2_项模板/0_Tree)
