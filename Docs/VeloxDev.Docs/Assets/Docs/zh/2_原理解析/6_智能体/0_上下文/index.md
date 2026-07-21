# 渐进式上下文

`WorkflowAgentScope` 提供渐进式上下文提示，帮助 LLM 理解工作流拓扑。上下文由 `AgentContextCollector` 收集，包含以下维度：

---

## 上下文组成

### 1. 当前图拓扑状态

`ProvideProgressiveContextPrompt()` 返回的文本描述包含：
- **节点列表**：每个节点的类型、`[VeloxProperty]` 属性值、Anchor 位置、Slot 数量
- **连接关系**：每个 Link 的 Sender → Receiver 映射
- **可用工具**：当前可调用的 AI 工具列表

### 2. 自动发现（WithAutoDiscovery）

```csharp
var scope = tree.AsAgentScope()
    .WithAutoDiscovery("VeloxDev.Core"); // 扫描指定程序集
```

`AgentContextCollector` 扫描程序集中标记了以下特性的类型：
- `[AgentContext]` — 描述文本
- `[AgentCommandParameter]` — 命令参数类型
- `[SlotSelectors]` — 条件 Slot 枚举
- `[WorkflowBuilder.Tree/Node/Slot/Link]` — 工作流组件

### 3. VeloxDev.Core 全部 AI 标注能力

所有 AI 相关特性均定义在 `VeloxDev.Core.AI` 命名空间中，不依赖 Extension 层。

| 特性 | 目标 | AllowMultiple | 说明 |
|------|------|:---:|------|
| `[AgentContext(AgentLanguages, string)]` | 类/属性/方法/字段/参数等（`AttributeTargets.All`） | ✅ | 为智能体提供多语言描述 |
| `[AgentCommandParameter]` | 属性/方法/字段 | ❌ | 声明命令无需参数 |
| `[AgentCommandParameter(typeof(T))]` | 属性/方法/字段 | ❌ | 声明命令参数类型为 T |
| `[SlotSelectors(typeof(Enum)[])]` | SlotEnumerator 属性 | ❌ | 声明条件 Slot 的允许枚举类型 |

**`[AgentContext]` 支持的 33 种语言**（`AgentLanguages` 枚举）：

`English`, `ChineseSimplified` / `Chinese`, `ChineseTraditional`, `Japanese`, `Korean`, `French`, `German`, `Spanish`, `Portuguese`, `Russian`, `Arabic`, `Hindi`, `Bengali`, `Urdu`, `Indonesian`, `Malay`, `Vietnamese`, `Thai`, `Turkish`, `Italian`, `Dutch`, `Polish`, `Czech`, `Swedish`, `Danish`, `Norwegian`, `Finnish`, `Greek`, `Hebrew`, `Romanian`, `Hungarian`, `Ukrainian`, `Persian`

### 4. 框架内置类型

Scope 自动识别以下框架类型：

| 分类 | 类型 |
|------|------|
| **组件** | `TreeDefaultViewModel`, `NodeDefaultViewModel`, `SlotDefaultViewModel`, `LinkDefaultViewModel` |
| **数据** | `Anchor`, `Offset`, `Size` |
| **枚举** | `SlotChannel`, `SlotState` |
| **接口** | `IWorkflowTreeViewModel`, `IWorkflowNodeViewModel`, `IWorkflowSlotViewModel`, `IWorkflowLinkViewModel` |

### 5. 自定义类型注册

```csharp
scope
    .WithComponents(typeof(MyNode))       // 注册自定义节点类型
    .WithInterfaces(typeof(IMyService))   // 注册自定义接口
    .WithData(typeof(MyDto))             // 注册数据对象
    .WithEnums(typeof(MyEnum));          // 注册枚举类型
```

### 6. 嵌入式资源

`AgentEmbeddedResources` 提供内建的提示模板资源，被 `ProvideProgressiveContextPrompt()` 自动引用以生成格式一致的系统提示。

### 7. 示例上下文输出

```
当前工作流拓扑：
- 节点 A（Controller, 位置: 100,200）
  - 输出 Slot: Slot[0]（已连接 → 节点 B）
- 节点 B（Processor, 位置: 400,200）
  - 输入 Slot: Slot[0]（来自节点 A）
  - 属性: delay = 1200ms, title = "处理步骤"

可用工具：
- CreateAndConfigureNode: 创建新节点
- ConnectSlots: 连接两个 Slot
- PatchNodeProperties: 修改节点属性
- ...
```
