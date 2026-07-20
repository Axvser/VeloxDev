# Aspect-Oriented Programming

> **💡V5.4.0 has undergone API changes. You can go to the [Version] section to view.**

.NET5+ natively supports proxy-based aspect-oriented programming, which means that without modifying the source code of a type, dynamic interception and customization of the behavior of properties and methods can be achieved. VeloxDev provides source code generation for it.

> **Build**

```csharp
public partial class MyViewModel
{
    [AspectOriented]
    public int Value { get; set; }
    
    [VeloxProperty]
    [AspectOriented]
    private string _name = string.Empty; // Compatibility field writing for MVVM generator

    [AspectOriented]
    public void Work()
    {

    }
}
```

> **Set Proxy**

When setting up a proxy, there are five key parameters.

| Parameter Name | Type | Description |
| --- | --- | --- |
| memberType | ProxyMembers | Member Type |
| memberName | string | Member Name |
| start | ProxyHandler | Pre-hook |
| coverage | ProxyHandler | Override hook |
| end | ProxyHandler | Post-hook |

Use these parameters in the SetProxy method

```csharp
public void LoadProxy(MyViewModel data)
{
    var proxy = data.Proxy; // get proxy
    
    // pre-hook
    data.Proxy.SetProxy(ProxyMembers.Getter,
        nameof(MyViewModel.Value),
        (p, r) =>
        {
            _manager.Show(new Notification("Message", $"a read operation happened at [{DateTime.Now}]"));
            return null;
        },
        null,
        null);
    
    // post-hook
    data.Proxy.SetProxy(ProxyMembers.Setter,
        nameof(MyViewModel.Name),
        null,
        null,
        (p, r) =>
        {
            _manager.Show(new Notification("Message", $"the name of team has been changed to {p?[0]}"));
            return null;
        });

    // override original logic
    data.Proxy.SetProxy(ProxyMembers.Method,
        nameof(MyViewModel.Work),
        null,
        (p, r) =>
        {
            _manager.Show(new Notification("Message", $"the default Reset() has been cancle"));
            return null;
        },
        null);
}
```

ProxyHandler signature explanation

| Parameter Name | Type | Description |
| --- | --- | --- |
| parameters | object?[]? | Parameters received by the entry, such as the value keyword in properties, parameters obtained during method invocation |
| previous | object? | Parameters returned by the previous lifecycle |

> **Access**

Properties must be accessed through a proxy for the hooks to take effect. In practice, you can choose as needed.

```javascript
var vm = new MyViewModel();
LoadProxy(vm);

var name = vm.Proxy.Name; // can trigger hooks

vm.Proxy.Work(); // can trigger hooks
```