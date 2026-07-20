First, for the new interface definition, when you want to use a custom type to create a branch with multiple connectors, you can implement the ISlotProvider interface.

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

Thus, you can easily implement a conditional provider for presenting multiple connectors at once.

```csharp
[AgentContext(AgentLanguages.Chinese,
    "Custom route selector (implements ISlotProvider). Before using, call GetTypeSchema('Demo.ViewModels.CustomRouteSelector') to understand its property structure, " +
    "then construct JSON based on the schema and pass it to SetEnumSlotCollection.")]
public class CustomRouteSelector : ISlotProvider
{
    public List<RouteEntry> Routes { get; set; } = [];

    public IEnumerable<SlotDefinition> GetSlots()
        => Routes.Select(r => new SlotDefinition(r.Key, r.Label));
}
```

Finally, when you want to have custom conditional routing within the Node component, you can use the previously defined CustomRouteSelector to dynamically generate it. Once defined, the agent will understand the CustomRouteSelector construction method based on serialization, and through the creation and passing of instance objects, it will achieve dynamic connector generation.

```csharp
[VeloxProperty]
[SlotSelectors(typeof(CustomRouteSelector))]
public partial SlotEnumerator<SlotViewModel> OutputSlots { get; set; }
```