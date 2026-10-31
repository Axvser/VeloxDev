# Properties

## The generated property

From `[VeloxProperty] private int _index = 0;`:

```csharp
public int Index
{
    get => this._index;
    set
    {
        if (global::System.Object.Equals(this._index, value)) return;
        var old = this._index;
        OnPropertyChanging(nameof(Index));
        OnIndexChanging(old, value);
        this._index = value;
        OnIndexChanged(old, value);
        OnPropertyChanged(nameof(Index));
    }
}
partial void OnIndexChanging(int oldValue, int newValue);
partial void OnIndexChanged(int oldValue, int newValue);
```

Generated output renders types fully qualified — `System.Int32`, `System.String`, and so on.

When nothing in the hierarchy already supplies the notification plumbing, the class also receives `INotifyPropertyChanging` / `INotifyPropertyChanged`, the `PropertyChanging` / `PropertyChanged` events, and `public virtual void OnPropertyChanging(string)` / `OnPropertyChanged(string)` (non-virtual when the class is sealed). A base that already supplies them is reused.

⚙ **The callbacks take `(oldValue, newValue)` in one method.** That is the true signature; do not carry over CommunityToolkit's `OnNameChanging(value)`.

⚙ **Naming:** one leading `_` is dropped and the next character upper-cased; otherwise just the first character. `_index` → `Index`, `title` → `Title`, `intervalMilliseconds` → `IntervalMilliseconds`, `m_value` → `M_value`. There is no camel-case splitting: `user_name` → `User_name`, not `UserName`.

## The two forms

```csharp
[VeloxProperty] private string _name = string.Empty;                  // field form
[VeloxProperty] public partial SlotViewModel InputSlot { get; set; }  // property form
```

⚙ The field form always emits a `public` property. The property form preserves the accessibility and accessor visibility you declared.

⚙ The property form synthesises a field by lower-casing only the first character: `InputSlot` → `_inputSlot`, `URL` → `_uRL`. For a workflow slot property, generated code depends on that exact name (`InitializeWorkflowCore` references it), so a hand-written field of a different name breaks it silently. Elsewhere `MVVMWriter.ResolveBackingStorage` emits its own field when the conventional `_camelCase` field is absent: an accessible field of that name is reused; otherwise one is emitted as `private T _name = <default>;`.

⚙ `GetDefaultValue` compares keyword spellings against the rendered `System.*` names, so `bool` / `int` / `string` come out as `default(System.Boolean)` / `default(System.Int32)` / `default(System.String)`, not `false` / `0` / `string.Empty`.

## The default setter

⚙ **The equality guard short-circuits on `Object.Equals(field, value)`**, except for `float` / `double` (including their nullable forms), which compare bit patterns so `-0.0` and the various `NaN` values are not collapsed.

⚙ **Setter order.** Capture old → slot uninstall → `OnPropertyChanging(name)` → `OnXChanging(old, value)` → collection unsubscribe (and `OnItemRemovedFromX`) → assign → slot install → collection subscribe (and `OnItemAddedToX`) → `OnXChanged(old, value)` → `OnPropertyChanged(name)`. A lifecycle step that fires at the wrong time is explained by that order.

That is `SetterMode.Default`; on the `FrameworkSetProperty` path (CommunityToolkit/Prism) there is no `Object.Equals` guard — `SetProperty(ref field, value, nameof(X))` does the comparison and assignment — and the slot-install, collection-subscribe and `OnXChanged` steps run inside its `if`. See [interop-and-diagnostics.md](interop-and-diagnostics.md).

## Collections

A property whose type implements `INotifyCollectionChanged` gets extra members:

```csharp
partial void OnItemAddedToItems(IEnumerable<Item> items);
partial void OnItemRemovedFromItems(IEnumerable<Item> items);
partial void OnItemMovedInItems(IEnumerable<Item> items);
partial void OnItemsResetInItems();
```

and, unless a base already declares a matching method, a `protected virtual void OnCollectionChanged<T>(string propertyName, NotifyCollectionChangedEventArgs e, IEnumerable<T>? oldItems, IEnumerable<T>? newItems)`.

⚙ **The four `OnItem…` hooks are the ergonomic path**; use `OnCollectionChanged<T>` when you need the raw event args. `Replace` calls the removed hook then the added hook; `Reset` calls `OnItemsResetInItems`.

⚙ **Base-class detection is loose.** The generator treats a base as already supplying `OnCollectionChanged<T>` when it finds a non-static generic method with one type parameter, four parameters, and a `string` first parameter. It does not compare the other three parameter types.

⚙ **The getter installs a subscription through `ObservableCollectionTracker.EnsureSubscribed`.** A collection initialised with `= []` is still observed even though the field initializer bypasses the setter. Replacing the collection unsubscribes the old instance and subscribes the new one. Deduplication is by `(Method, Target)`, and entries live in a `ConditionalWeakTable`.

⚙ **A collection hook needs a settable property.** The hooks and the private `On{Name}CollectionChanged` handler are emitted only when the property has a setter, but the getter still calls `EnsureSubscribed(..., On{Name}CollectionChanged)`. A get-only collection property therefore fails to compile with `CS0103`.
