> **VeloxDev.Core.Extension 提供的序列化依赖反射，而智能体的部分能力依赖序列化，若您在裁剪敏感的环境下遇到了无法运行Agent或者无法执行序列化，可以试着关闭裁剪**

```csharp
<PublishTrimmed>false</PublishTrimmed>
```