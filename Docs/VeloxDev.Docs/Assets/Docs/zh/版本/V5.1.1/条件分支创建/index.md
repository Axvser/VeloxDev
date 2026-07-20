首先，是全新的接口定义，当你希望使用自定义类型来创建具备多个连接器的分支时，可以实现 ISlotProvider 接口

```csharp
public sealed class SlotDefinition
{
    public object Value { get; }

    public string Label { get; }

    public SlotDefinition(object value, string label = "")
    {
        Value = value;
        Label = label;
    }
}

public interface ISlotProvider
{
    IEnumerable<SlotDefinition> GetSlots();
}
```

于是，你就能轻松实现一个用于一次性呈现多个连接器的条件提供者

```csharp
[AgentContext(AgentLanguages.Chinese,
    "自定义路由选择器（实现 ISlotProvider）。使用前先调用 GetTypeSchema('Demo.ViewModels.CustomRouteSelector') 了解其属性结构，" +
    "再根据 schema 构造 JSON 传给 SetEnumSlotCollection。")]
public class CustomRouteSelector : ISlotProvider
{
    public List<RouteEntry> Routes { get; set; } = [];

    public IEnumerable<SlotDefinition> GetSlots()
        => Routes.Select(r => new SlotDefinition(r.Key, r.Label));
}
```

最终，当你在Node组件内希望拥有自定义的条件路由时，可使用刚才定义的 CustomRouteSelector 来动态生成，一旦定义，智能体将基于序列化自行理解 CustomRouteSelector 构造方式，并基于实例对象的创建与传递，实现动态连接器生成

```csharp
[VeloxProperty]
[SlotSelectors(typeof(CustomRouteSelector))]
public partial SlotEnumerator<SlotViewModel> OutputSlots { get; set; }
```