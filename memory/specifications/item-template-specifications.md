# .NET Item Template

> 用户口述、agent 落笔。约束这个仓库里的**模板项目**（`Src/Templates/`）以及**与它对应的 demo**。
> **触发**：改动 `Src/Templates/` 下任何文件之前；判断「这处改动该落在模板还是 demo」之前；新增 / 改名 / 删除一个模板项之前。
> 模板**怎么实现**（内容项的结构、符号、生成物长什么样）不在这里，在 `memory/modules/Templates/`。

---

## 一、模板是源，Trimmed demo 是它的镜像

- `Src/Templates/VeloxDev.<平台>.Templates/working/content/<项>/` 是**源**：用户 `dotnet new` 生成进自己的项目，拿到的是这里的写法。
- `Examples/Workflow/<平台> Trimmed/Demo/` 与它**一一对应**，是镜像。
- ⇒ **要改就改模板，Trimmed demo 不单独改。** 只在 demo 上改会让两边分叉 —— 而用户消费的是模板那一份，改在 demo 上的那半他永远拿不到。
- 模板改了，Trimmed demo 要跟着保持一致（两边同形）；反过来，只发现 demo 有问题时，先回去看模板是不是也错。

---

## 二、七家的模板项清单必须一致

七家模板发这七项，名字一致：

`workflow-grid-decorator`、`workflow-link-view`、`workflow-minimap-overlay`、`workflow-node-view`、`workflow-slot-view`、`workflow-template-selector`、`workflow-tree-view`

⇒ 「少一项」与「这家没有差异」是两件事：少一项等于用户在这家拿不到那个扩展点。

---

## 三、自定义模板选择器：家家都要支持，且优先级最高

- 七家都必须支持**用户自己的模板选择器** —— 是 VeloxDev 自己的那套 API（`ViewPool.TemplateSelector` / `ViewSourcePipeline` 的构造参数），不是平台自带的 `DataTemplates` / `DataTemplateSelector` 机制。平台自带的那套继续兜底。
- 模板的 tree-view 里要把它**接上**：XAML 平台写 `behaviors:ViewPool.TemplateSelector="{StaticResource …}"`；WinForms 在 tree 的资源里声明 `<local:TemplateSelector x:Key="…" />`，根上写 `behaviors:WorkflowSurfaceBehavior.TemplateSelector="{StaticResource …}"`；Razor 写组件 `<TemplateSelector … />`。
- **选择器的优先级最高**：解析一个 VM 的视图时先问它，它匹配就用它；平台自带的模板查找只在它之后兜底。

理由两条：它是七家**公共 API 面**的一部分（用户换一家平台不该换一套写法），而客制化时用户最先要动的正是「哪个 VM 用哪个视图」。

---

## 四、模板里的注释

模板注释**只解释扩展点，永远不解释机制、原理** —— 细则见 [code-comment-specifications.md](code-comment-specifications.md) §五。

**七家的注释保持同一粒度**：同一个条目在每家都只留同一组一句话提示；不要一家写成机制长文、另一家只留一句。平台差异本身需要说明时，压成一行「为什么这里写法不同」，其余推导留在模块记忆或提交信息。

---

## 五、现状：与第三节不一致的地方

**这一节是现状，不是规范。** 要动相关代码时照第三节的方向改。

- **Avalonia 的建视图路径是 `template.Build(item)`**（`Src/Adapters/VeloxDev.Avalonia/WorkflowSystem/ViewHosting/ViewFactory.cs` 的 `CreateView`），而 Avalonia 的 `IDataTemplate` 是「既选又建」—— `Match` 挑出的是选择器自己，轮到 `Build` 时它才去挑内层模板；传 `null` 就无从下手（选择器的 `SelectTemplate(null)` 抛异常）⇒ **视图一个都不建，且不报错**（画布空白）。传 VM，选择器因此可用。
- **判定顺序**（四家 `FindDataTemplate` 同形）：按 VM **类型**的缓存 → 选择器 → 平台自带的查找（Avalonia：`ViewFactory.FindTemplate` 的缓存 → 选择器 → 面板 / 祖先 / `Application`）⇒ 「选择器命中就跳过平台机制、没命中就退化到平台机制」成立；但选择器若**按实例**判定，第一个实例的判定会被整个类型沿用。
- **WinForms 的池没有平台兜底**：选择器是唯一的创建路径（`Src/Adapters/VeloxDev.WinForms/Xaml/TemplateSelector.cs` 的 `SelectTemplate(item)` → `DataTemplate`），所以「退化到平台机制」在那家不存在。
- **WinForms 的连线默认不池化**：`ViewLinkLayer.IsActive=true`，`workflow-tree-view` 在 `PART_Canvas` 上挂 `behaviors:WorkflowLinkLayer.*`，由它一趟画完所有可见线；`workflow-link-view` 条目仍存在，但只是显式 per-link opt-in，默认 tree 不实例化它。其余平台仍按角色池化连线（MAUI 是单 overlay 的例外）。
- **连线的命中与删除是库能力；高亮等外观是 demo 专属**（2026-10-04 用户改定。分层的完整规则见 [layer-ownership-specifications.md](layer-ownership-specifications.md)）：连线视图只负责**画出自己那条曲线并把它发布出去**，其余由 Core、适配器、demo 三方分担 ——
  - 命中判定归 Core（`LinkHitTestEx` 对着已发布的曲线判距，半径 `LinkHitTestEx.DefaultHitRadius`）；
  - **输入路由只有一个位置**：`WorkflowInput.For(tree)`（一棵树一个实例），适配器把原生指针/按键翻译成标准输入后只往它里面转发；宿主与连线视图都在**组件的 Helper**上订（`IInputEvents`）；
  - **删除也是 demo 的**（2026-10-04 改定）：库只把 `Delete` 键路由过来（target = 指针停着的那条线），宿主订 `KeyDown` 自己执行 `link.DeleteCommand` —— 与悬停高亮同一条路。**菜单里的条目同理**：模板不再自带 `Delete` 项，基类也不加；
  - **悬停/选中高亮归 demo**：订那条线自己的 `Input.PointerEntered` / `PointerExited`（路由保证「先 Exited 后 Entered」）自己画。模板里的连线视图是**被动视觉**，不含任何高亮外观；适配器给的是**指针语义的钩子**加一个空的画法钩子（WinForms 的 `WorkflowLinkAttachment`：指针事件 `PointerEntered` / `PointerLeft` / `PointerPressed` / `PointerReleased`，画法 `Paint(Graphics)`）。Core 里没有 `ILinkHighlight`、也没有 `AutoHighlight`。
  ⇒ 判据是「这个角色**有没有指针源**」：任何一家只要它的连线视图或表面把指针位置喂给了 hub，生成的工程就开箱有命中/删除；**高亮要自己按 demo 那份写**。
  - **连线的右键菜单也归模板**（2026-10-03 用户改定，推翻本条原来那句「右键菜单仍是 demo 的策略，不要往模板里推」）：
    理由直说 —— **条目列表本身就是用户要改的东西**，「改模板增删菜单条目」比「读库的文档再自己接一套」直接得多。
  - 落点是 **`workflow-tree-view` 条目**、不是 `workflow-link-view`：右键落在**表面**上（连线视图在很多家不吃指针），
    而弹出位置要**屏幕坐标**（模型给的是画布坐标），只有表面同时知道这两件事。
  - **接线必须在适配层，模板只声明**（2026-10-03 用户改定）：**条目**归用户（声明在 `workflow-tree-view` 的资源里），
    而**订阅 / 定位 / 弹出 / 开合上报一行都不许留在模板**。能声明附着属性的平台用
    `behaviors:WorkflowSurfaceBehavior.LinkMenuKey="<资源键>"`，与 `ViewPool.TemplateSelector` 同一条线；
    WinForms 没有可声明委托的标记形态，宿主在 code-behind 调
    `WorkflowSurfaceBehavior.SetLinkMenuBuilder(this, …)` 提供条目；适配器负责订阅、定位、弹出与开合上报。
    ⇒ **模板产物的 code-behind 因此只剩 `InitializeComponent()`。**
    **为什么传资源键、不传菜单本身**：这个属性挂在表面**自己的根元素**上，`{StaticResource}` 在那里会在
    定义它的那个资源字典之前求值；键交给适配器在挂载之后解析，绕开这个顺序。
  - 触发点是**这条连线上的右键按下**（**可取消**：宿主在链上更靠前的一级 —— 连线自己 —— 订同一个事件并
    `PreventDefault` 就是「这里不给菜单」，顺序由「目标先于祖先」保证），
    挂起用 `WorkflowInput.IsSuspended` 表达 —— 菜单开着时指针跟踪不动。
  - **WinForms**：条目由宿主在 code-behind 交给 `WorkflowSurfaceBehavior.SetLinkMenuBuilder(this, …)`；
    适配器负责订阅、定位、弹出与开合上报，模板只保留 `InitializeComponent()`。
  ⇒ 反过来，**不要**为了「能点到连线」把连线视图改成吃掉整块画布的命中面（那会吞掉画布手势）；命中面必须仍然只是画出来的那道描边。

---

## 六、七个角色的统一标准

> 用户口述（2026-11-24）。七家按同一张表交付；缺一项或写法走样都算不符。

| 角色 | 标准 |
|---|---|
| TreeView | code-behind 极简到只有一个 `InitializeComponent()` 调用，不许有别的；UI 由 XAML 完成，交互全部由附加属性负责添加 |
| TemplateSelector | 不预留属性，直接在函数里写返回；只做 Node / Link / Slot，Tree 不做 |
| SlotView | 同 TreeView |
| NodeView | 在 TreeView 的要求之上，预设左（单 slot）右（动态多 slot）槽位的渲染绑定 |
| MinimapOverlay | 适配器层允许提供基类；模板必须把底层绘制暴露成可重写函数，且函数体默认写上 `base` 调用；该绘制函数要包含小地图所需的全部上下文。可行时在 Core 添加通用接口 / 管道、七家适配器统一实现（或仿照 grid-decorator：通用数据交换接口 + 自绘） |
| LinkView | 各家自绘，但必须采用同一套属性定义来做绑定 |
| GridDecorator | 同 LinkView；并且必须挂着 `IWorkflowGridDecorator` 接口来统一同步数据 |

### 6.1 显式例外

下面这些是**平台硬限制**造成的偏离，不算违反上表；新增平台不许拿它们当先例。

| 平台 | 角色 | 偏离 | 硬限制 |
|---|---|---|---|
| Jalium | TreeView | code-behind 里有 `CanvasTransform` 镜像 DP 与 CLR 属性 | 绑定路径不解析 `(附加属性)` 括号语法，树只能把画布变换重新暴露成普通属性；交互仍全部由附着行为负责 |
| MAUI | LinkView | 没有 per-link 端点属性，改用 overlay 级属性 | 每线一个 `GraphicsView` 在深缩放会超出 Win2D 纹理上限并静默消失，只能一个视口级 overlay 画完所有线 |
| MAUI | NodeView | code-behind 有按宽度缩放的 `OnSizeAllocated` | 没有 `Viewbox`；渲染变换不参与布局，会破坏槽锚点测量，只能走布局尺寸 |
| Razor | 全部 | 没有 `InitializeComponent`；组件参数与 `.razor.cs` 是声明路径 | Blazor 组件模型没有「标记 + 代码后置」的对应物 |
| Razor | MinimapOverlay | 扩展点是 `DrawMinimap` render fragment，不是函数重写 | 组件无法覆写基组件的渲染树 |
| Razor | LinkView | 端点属性是可选的覆盖值，默认从模型读 | 池化视图重渲染时父级不会重新求值传给它的参数，几何必须留在视图内读模型 |
| Razor | GridDecorator / MinimapOverlay | `IWorkflowGridDecorator` / `IWorkflowMinimapOverlay` 挂在适配器组件上，模板条目是 wrapper | Blazor 用组件参数（`SurfaceViewport`）推送数据，没有附着属性那条管道 |
