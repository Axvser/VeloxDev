源生成器不再对视图模型生成名为 Proxy 的属性，而是在保留原有接口动态创建的前提下增加扩展方法和全局缓存管理，这是为了彻底消除潜在的继承链破坏风险，API现已发生重大变更

> **接口变更**

IProxy接口已重命名，这是为了避免与网络代理等概念产生歧义

```csharp
namespace VeloxDev.AspectOriented
{
    public interface IAspectOriented
    {

    }
}
```

> **代理对象访问**

内置弱引用字典，用Aop()访问代理时会查询字典缓存，若不存在则自动构建

```csharp
private void ConfigureAOP(TeamViewModel data)
{
    var p = data.Aop(); // 等价于低版本的 data.Proxy
}
```

> **原始数据查找**

极少数情况下会需要反向查找代理对象的代理目标，返回值是可空的

```csharp
private void GetSourceData(IAspectOriented ao)
{
    var data = Aop.GetTarget<TeamViewModel>(ao);
}
```