# Component

A workflow is a graph structure containing four important components. This chapter will show you how to build the view models corresponding to the components.

> **Component Overview**

| Component Type | Parent Type | Description |
| --- | --- | --- |
| Tree |  | Global container, stores all data of a single workflow |  |
| Node | Tree | Node, carries configuration and business execution |  |
| Slot | Node | Connector, carries connection relationships and channel direction restrictions |  |
| Link |  | Connection line, can access the Slot of the start point and end point |  |

> **Core Features**

Next, all view models will be built based on the following features, $component is the component type, $helper is the injected business logic.

```csharp
【WorkflowBuilder.{$component}<{$helper}>】
```