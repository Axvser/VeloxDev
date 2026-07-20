# Grid Decorator

> **Create - {$plateform} optional ava / wpf / winui / maui**

```shellscript
dotnet new {$plateform}-v-decorator -n GridDecorator -ns MyApp.Views
```

> **Personality parameters**

| Option | Meaning |
| --- | --- |
| -bg | Background color |
| -mic | Secondary color |
| -mac | Main color |
| -ac | Axis color |
| -gs | Spacing |
| -mle | Main spacing |

> **Interface**

The grid decorator communicates data with the adapter based on the following interface. When the user interacts, the four properties here will be updated as necessary. Only the drawing logic of the decorator itself needs to be considered.

```csharp
public interface IWorkflowGridDecorator
{
    double ScrollOffsetX { get; set; }
    double ScrollOffsetY { get; set; }
    double ContentOffsetX { get; set; }
    double ContentOffsetY { get; set; }
}
```