# 面向切面编程

> **💡V5.4.0已发生API变更，可前往【版本】章节查看**

.NET5+ 原生支持基于代理的面向切面编程，这意味着，不修改类型源码，也能做到动态拦截、自定义属性和方法的行为，VeloxDev 为其配套了源代码生成

> **构建**

```csharp
public partial class MyViewModel
{
    [AspectOriented]
    public int Value { get; set; }
    
    [VeloxProperty]
    [AspectOriented]
    private string _name = string.Empty; // 兼容MVVM生成器的字段写法

    [AspectOriented]
    public void Work()
    {

    }
}
```

> **设置代理**

设置代理时，有五个关键参数

| 参数名 | 类型 | 描述 |
| --- | --- | --- |
| memberType | ProxyMembers | 成员类型 |
| memberName | string | 成员名 |
| start | ProxyHandler | 前置钩子 |
| coverage | ProxyHandler | 覆盖钩子 |
| end | ProxyHandler | 后置钩子 |

在SetProxy方法中使用这些参数

```csharp
public void LoadProxy(MyViewModel data)
{
    var proxy = data.Proxy; // 获取代理
    
    // 前置钩子
    data.Proxy.SetProxy(ProxyMembers.Getter,
        nameof(MyViewModel.Value),
        (p, r) =>
        {
            _manager.Show(new Notification("Message", $"a read operation happened at [{DateTime.Now}]"));
            return null;
        },
        null,
        null);
    
    // 后置钩子
    data.Proxy.SetProxy(ProxyMembers.Setter,
        nameof(MyViewModel.Name),
        null,
        null,
        (p, r) =>
        {
            _manager.Show(new Notification("Message", $"the name of team has been changed to {p?[0]}"));
            return null;
        });

    // 覆写原逻辑
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

ProxyHandler 签名解释

| 参数名 | 类型 | 描述 |
| --- | --- | --- |
| parameters | object?[]? | 入口接收到的参数，例如属性中的value关键词、方法调用时获取的参数 |
| previous | object? | 上一个生命周期返回的参数 |

> **访问**

必须通过代理访问属性才能令钩子生效，实际业务中，您可按需选择

```csharp
var vm = new MyViewModel();
LoadProxy(vm);

var name = vm.Proxy.Name; // 可触发钩子

vm.Proxy.Work(); // 可触发钩子
```