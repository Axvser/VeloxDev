The source generator no longer generates a property named Proxy for view models. Instead, while retaining dynamic creation of the original interface, it adds extension methods and global cache management. This is to completely eliminate the potential risk of breaking the inheritance chain. The API has undergone a major change.

> **API Changes**

IProxy interface has been renamed to avoid ambiguity with network proxy and other concepts.

```csharp
namespace VeloxDev.AspectOriented
{
    public interface IAspectOriented
    {

    }
}
```

> **Proxy Object Access**

Built-in weak reference dictionary. When accessing a proxy via Aop(), it queries the dictionary cache; if not present, it is automatically constructed.

```csharp
private void ConfigureAOP(TeamViewModel data)
{
    var p = data.Aop(); // Equivalent to the older version of data.Proxy
}
```

> **Raw Data Search**

In very rare cases, you may need to reverse-lookup the proxy target of a proxy object; the return value is nullable.

```csharp
private void GetSourceData(IAspectOriented ao)
{
    var data = Aop.GetTarget<TeamViewModel>(ao);
}
```