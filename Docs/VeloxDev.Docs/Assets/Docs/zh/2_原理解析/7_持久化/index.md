# 持久化架构

使用 `Newtonsoft.Json` 通过 `VeloxDev.Core.Extension` 的扩展方法进行工作流序列化。序列化器理解完整的组件图 — Tree、Nodes、Slots、Links 及其 `[VeloxProperty]` 值。

---

## 序列化图

```mermaid
flowchart TB
    subgraph JSON[序列化 JSON]
        Tree[Tree\nLayout, VirtualLink]
        Nodes["Nodes[]\nAnchor, Size"]
        Links["Links[]\nSender, Receiver"]
        Maps["LinksMap\nSlot→Slot→Link"]
    end

    subgraph Memory[运行时对象]
        T[TreeViewModelBase]
        N[NodeViewModelBase\nSlots[], Parent]
        L[LinkViewModelBase\nSender, Receiver]
        S[SlotViewModelBase\nTargets, Sources]
    end

    T --> Tree
    N --> Nodes
    L --> Links
    S -.->|通过 Parent| N
    Tree --> Links --> L
    Nodes --> N
    Tree --> Nodes
```

## 扩展方法 API

| 类别 | 方法 | 描述 |
|------|------|------|
| 同步 | `tree.Serialize()` | 序列化为 JSON 字符串 |
| 同步 | `json.Deserialize<T>()` | 反序列化（失败抛异常） |
| 同步 | `json.TryDeserialize<T>(out var)` | 安全反序列化 |
| 异步 | `await tree.SerializeAsync()` | 异步序列化 |
| 异步 | `await json.DeserializeAsync<T>()` | 异步反序列化 |
| 格式化 | `SerializationOptions.Create().WithIndented()` | 缩进输出 |
| 字节 | `tree.SerializeToUtf8Bytes()` | 序列化为 UTF8 字节 |
| 流 | `stream.SerializeToStreamAsync()` | 序列化到流 |

## 序列化的内容

| 组件 | 属性 |
|------|------|
| **Tree** | Layout（画布尺寸、视口偏移）、VirtualLink |
| **Node** | Anchor（x, y, layer）、Size、所有 `[VeloxProperty]` 字段 |
| **Slot** | Anchor、Channel、State、Targets（slot ID）、Sources（slot ID） |
| **Link** | Sender（slot ID）、Receiver（slot ID）、IsVisible |
| **自定义数据** | 所有 `[VeloxProperty]` 值，包括业务特定数据 |

## 跨平台

同一份 JSON 可在 **Desktop**、**Browser** 或 **Mobile** 上加载 — 支持云端持久化。序列化与 GUI 框架无关，仅依赖运行时对象结构。
