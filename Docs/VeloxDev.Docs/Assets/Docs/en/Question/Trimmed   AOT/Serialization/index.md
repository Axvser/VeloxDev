> **The serialization provided by VeloxDev.Core.Extension relies on reflection, and some capabilities of the agent rely on serialization. If you encounter an inability to run the Agent or perform serialization in a trimming-sensitive environment, you can try disabling trimming.**

```csharp
<PublishTrimmed>false</PublishTrimmed>
```