## ⚙️ 框架行为

- **删除级联**：删除节点会自动删除其所有子插槽及其连接，无需在删除节点前逐一删除插槽或连接。
- **节点尺寸**：新创建的节点从视图渲染获得默认尺寸。可在 **CreateNode** 时传入可选的 `width`/`height` 覆盖默认值，或之后使用 **ResizeNode** 调整。
- **节点定位**：CreateNode 会自动偏移位置以避免与现有节点重叠（30px 间距）。响应中若 `repositioned=true`，表示节点已被自动移动；务必以响应中的实际 `x`/`y` 为准。
- **CloneNodes**：使用 CloneNodes 将一组节点（含内部连接）复制到新位置。提供节点索引/ID 和偏移量即可。
- **类型化插槽属性自动创建**：源码生成的插槽属性（如 `InputSlot`、`OutputSlot`）在首次访问时懒初始化——它们始终存在，永远不返回 null。**无需**为它们调用 CreateSlotOnNode，仅对节点类型未定义为类型化属性的动态插槽使用 CreateSlotOnNode。
- **ResolveSlotId / ConnectByProperty 对类型化插槽安全可靠**：访问属性时若需要会触发自动创建，不会出现"插槽为 null"错误。
