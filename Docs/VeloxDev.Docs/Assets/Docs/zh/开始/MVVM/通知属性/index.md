# 通知属性

> **字段**

```csharp
[VeloxProperty] private string name = string.Empty;
```

> **分部属性**

```csharp
[VeloxProperty] public partial string Name { get; protected set; }
```

> **回调**

```csharp
partial void OnNameChanged(string oldValue, string newValue)
{
    
}
```

> **INotifyCollectionChanged 特殊回调**

若成员派生自 INotifyCollectionChanged，会额外提供一些回调函数，具体来讲，当通知属性的值发生变化，内部自动处理回调函数的订阅与销毁。值得注意的是，分部属性无法在定义时声明初始值，因此推荐您在构造阶段初始化这些集合

```csharp
[VeloxProperty] public partial ObservableCollection<string> Values { get; set; }

partial void OnItemAddedToValues(IEnumerable<string> items)
{

}

partial void OnItemRemovedFromValues(IEnumerable<string> items)
{

}
```