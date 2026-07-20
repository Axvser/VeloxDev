# 业务

工作流所有组件的逻辑都是可注入、可替换的，并且提供事件、可重写方法来允许您进行定制，您定义的每个工作流组件都会持有一个 Helper 属性，可基于它访问业务实现

> **基类**

使用泛型版本后，所有 Helper 内置的 Component 属性将蜕变为具体的派生类，非泛型版本内部的 Component 属性是接口抽象形式

| 类型 | 泛型约束 | 说明 |
| --- | --- | --- |
| TreeHelper<T> | where T : IWorkflowTreeViewModel | 为全局容器提供扩展的行为 |
| NodeHelper<T> | where T : IWorkflowNodeViewModel | 为节点提供扩展的行为 |
| SlotHelper<T> | where T : IWorkflowSlotViewModel | 为连接器提供扩展的行为 |
| LinkHelper<T> | where T : IWorkflowLinkViewModel | 为连接线提供扩展的行为 |