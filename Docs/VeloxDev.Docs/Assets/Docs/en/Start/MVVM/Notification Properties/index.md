# Notification Properties

> **field**

```csharp
[VeloxProperty] private string name = string.Empty;
```

> **Distribution Properties**

```csharp
[VeloxProperty] public partial string Name { get; protected set; }
```

> **Callback**

```csharp
partial void OnNameChanged(string oldValue, string newValue)
{
    
}
```

> **INotifyCollectionChanged special callback**

If a member derives from INotifyCollectionChanged, some additional callback functions are provided. Specifically, when the value of a notifying property changes, the subscription and disposal of callback functions are handled internally. It is worth noting that partial properties cannot declare initial values at definition time, so it is recommended that you initialize these collections during the construction phase.

```csharp
[VeloxProperty] public partial ObservableCollection<string> Values { get; set; }

partial void OnItemAddedToValues(IEnumerable<string> items)
{

}

partial void OnItemRemovedFromValues(IEnumerable<string> items)
{

}
```