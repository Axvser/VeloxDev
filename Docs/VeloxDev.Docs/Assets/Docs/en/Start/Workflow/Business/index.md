# Business

All the logic of the workflow components is injectable and replaceable, and provides events and overridable methods for you to customize. Each workflow component you define will hold a Helper property, through which you can access the business implementation.

> **base class**

After using the generic version, all Component properties built into Helper will transform into concrete derived classes, while in the non-generic version, the Component property inside is an abstract interface form.

| Type | Generic Constraint | Description |
| --- | --- | --- |
| TreeHelper<T> | where T : IWorkflowTreeViewModel | Provides extended behavior for the global container |
| NodeHelper<T> | where T : IWorkflowNodeViewModel | Provides extended behavior for nodes |
| SlotHelper<T> | where T : IWorkflowSlotViewModel | Provides extended behavior for connectors |
| LinkHelper<T> | where T : IWorkflowLinkViewModel | Provides extended behavior for connection lines |