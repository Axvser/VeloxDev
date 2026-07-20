# Grid Decorator

> **创建 - {$plateform} 可选 ava / wpf / winui / maui**

```shellscript
dotnet new {$plateform}-v-decorator -n GridDecorator -ns MyApp.Views
```

> **个性参数**

| 选项 | 含义 |
| --- | --- |
| -bg | 背景色 |
| -mic | 次颜色 |
| -mac | 主颜色 |
| -ac | 轴线颜色 |
| -gs | 间距 |
| -mle | 主间距 |

> **接口**

网格装饰器基于下述接口与适配器进行数据互通，当用户交互，此处的四个属性均会在必要时更新，仅需关注装饰器自身绘制逻辑

```csharp
public interface IWorkflowGridDecorator
{
    double ScrollOffsetX { get; set; }
    double ScrollOffsetY { get; set; }
    double ContentOffsetX { get; set; }
    double ContentOffsetY { get; set; }
}
```