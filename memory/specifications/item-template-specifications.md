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

- 七家都必须支持**用户自己的模板选择器** —— 是 VeloxDev 自己的那套 API（`ViewPool.TemplateSelector` / `ViewManager` 的构造参数），不是平台自带的 `DataTemplates` / `DataTemplateSelector` 机制。平台自带的那套继续兜底。
- 模板的 tree-view 里要把它**接上**：`behaviors:ViewPool.TemplateSelector="{StaticResource …}"`（写法照已接上的三家：MAUI / WinUI / WPF 的 `workflow-tree-view`）。
- **选择器的优先级最高**：解析一个 VM 的视图时先问它，它匹配就用它；平台自带的模板查找只在它之后兜底。

理由两条：它是七家**公共 API 面**的一部分（用户换一家平台不该换一套写法），而客制化时用户最先要动的正是「哪个 VM 用哪个视图」。

---

## 四、模板里的注释

模板注释**只解释扩展点，永远不解释机制、原理** —— 细则见 [code-comment-specifications.md](code-comment-specifications.md) §五。

---

## 五、现状：与第三节不一致的地方

**这一节是现状，不是规范。** 要动相关代码时照第三节的方向改。

- **七家的接法各不相同，「接上了」不等于搜得到字面串**：只有 XAML 那几家写得出 `behaviors:ViewPool.TemplateSelector="{StaticResource …}"`；另外三家用别的形态 —— WinForms `ViewPool.SetTemplateSelector(PART_Canvas, _selector)`（`Src/Templates/VeloxDev.WinForms.Templates/working/content/workflow-tree-view/TemplateClass.cs:729`，这家没有附着属性）、Jalium 同名的 `ViewPool.SetTemplateSelector(this, TemplateSelector)` 配一个 public 属性（同文件 `:105-116`）、Razor 是组件 `<TemplateSelector Items="Tree.Nodes" KeySelector="n => n" … />`（`workflow-tree-view/TemplateClass.razor:46`）。⇒ **只 grep `ViewPool.TemplateSelector=` 会把 WinForms / Jalium / Razor 误判成没接。**
- **Avalonia 此前确实没接**，而且它的症结不在 tree-view，在适配器：池是 `template.Build(null)` 建视图（`Src/Adapters/VeloxDev.Avalonia/Attached/Workflow/ViewManager.cs:168`），而 Avalonia 的 `IDataTemplate` 是「既选又建」—— `Match` 挑出的是选择器自己，轮到 `Build` 时它才去挑内层模板；传 `null` 就无从下手（选择器的 `SelectTemplate(null)` 抛异常）⇒ **视图一个都不建，且不报错**（画布空白）。现在传的是 VM，选择器因此可用。
- **判定顺序**（四家 `FindDataTemplate` 同形）：按 VM **类型**的缓存 → 选择器 → 平台自带的查找（Avalonia：`ViewManager.cs:233` 缓存 → `:235` 选择器 → `:242`/`:248`/`:259` 面板/祖先/`Application`）⇒ 「选择器命中就跳过平台机制、没命中就退化到平台机制」成立；但选择器若**按实例**判定，第一个实例的判定会被整个类型沿用。
- **WinForms 是例外**：它的池没有平台兜底可谈 —— 选择器是唯一的创建路径（`Src/Adapters/VeloxDev.WinForms/Attached/Workflow/ViewManager.cs:161` 的 `_selector.CreateView(item)`），所以「退化」在那家不存在。
