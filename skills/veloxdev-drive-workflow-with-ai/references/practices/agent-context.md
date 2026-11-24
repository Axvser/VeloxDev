# Documenting the agent surface

The model only knows what the compiled context tree carries. Two forms write that documentation, and they
are equivalent at runtime:

- `[AgentContext]` on a type or member;
- a `*.agentcontext.xml` resource read at compile time.

Both become `AgentText` entries in the generated tree. `GetComponentContext`, `WithAutoDiscovery`,
`AgentContextCollector.GetClassContext` and the prompt renderers read the same tree, so the choice is about
where the text lives, not about what the model can see.

## The attribute

```csharp
[AgentContext(AgentLanguages.Chinese, "用来描述这个节点")]
[AgentContext(AgentLanguages.English, "Describes what this node is for")]
[WorkflowBuilder.Node<NodeHelper>]
public partial class MyNodeViewModel          // your own node; the generator supplies the component half
{
    public MyNodeViewModel() => InitializeWorkflow();

    [AgentContext(AgentLanguages.English, "The title shown on the card")]
    public string? Title { get; set; }
}
```

- The first positional argument is the language; the second is the text. `[AgentContext(AgentLanguages.Chinese)]`
  is legal but describes nothing.
- One target can carry several texts in one language; all are returned in order.
- A target with no text in the requested language falls back to English **as a whole**. A partially translated
  target does not mix languages, and English has nowhere to fall back to.
- Commands are declared on the interface: put the annotation on the `ICommand` property there. The generator
  copies it to the implementing command node. `[AgentCommandParameter(typeof(T))]` stays on the same property —
  it declares the parameter type, not a description.
- `[SlotSelectors]` is not documentation either. An enum only named by `[SlotSelectors]` is a *reference* in
  the tree, not a resolvable type entry; document the enum itself when the model may call
  `SetEnumSlotCollection` with it. A non-enum provider also needs `[Archivable]`.

## The XML resource

Name the file `*.agentcontext.xml`. The generator reads it as an `AdditionalFiles` item; it is a
**compile-time input**, not a runtime resource. The XML is not in the built assembly, so a text change needs a
rebuild.

The file needs no `<?xml version="1.0"?>` declaration — start directly at `<AgentContext>`:

```xml
<AgentContext>
  <Type name="My.Nodes.MyNodeViewModel">
    <Text language="Chinese">用来描述这个节点</Text>
    <Text language="English">Describes what this node is for</Text>

    <Member name="Title">
      <Text language="Chinese">卡片上显示的标题</Text>
      <Text language="English">The title shown on the card</Text>
    </Member>

    <Member name="Save">
      <Text language="English">Saves the current document</Text>
    </Member>
  </Type>
</AgentContext>
```

Rules:

- `language` is an `AgentLanguages` member name — `English`, `Chinese`, `ChineseSimplified`, … — matched
  case-insensitively. Omit it for `English`. Language codes such as `zh-Hans` are not accepted.
- Type names accept the source spelling (`My.Nodes.MyNodeViewModel`) or the metadata spelling
  (`My.Nodes+MyNodeViewModel`). Each assembly generates its own fragment, so a type from another assembly
  cannot be described from this project's resource.
- Member names match the tree's name or the source symbol's name. A `[VeloxProperty]` field can be addressed as
  `Payload` or `_payload`; a `[VeloxCommand]` method as `Save` or `SaveCommand`. An inherited member belongs to
  the type that declares it.
- Texts are **additive** with attributes: attributes come first, resource entries after. A type or member
  described only by the resource still enters the tree.
- A private field named by the resource becomes a description-only entry — it has no read/write accessor, the
  same as a field carrying `[AgentContext]` without `[VeloxProperty]`.

### Placement

- A resource under the project directory is picked up by the generator package's convention. The repository's
  `Src/Directory.Build.targets` and `Examples/Directory.Build.targets` repeat the same convention for
  project-reference builds.
- A resource outside the project directory — for example in the repository's `Assets/` folder — must be listed
  explicitly:

  ```xml
  <ItemGroup>
    <AdditionalFiles Include="..\..\..\Assets\MyPackage.agentcontext.xml" />
  </ItemGroup>
  ```

- If you also want the XML in the assembly, add the same file as `EmbeddedResource`. The generator still reads
  the `AdditionalFiles` item; the two items serve different readers.

## Which form

| Situation | Use |
|---|---|
| One type or member, with the text right next to the declaration | the attribute |
| Bulk descriptions, several languages, or keeping partial/generated view models short | the XML resource |
| Existing `[AgentContext]` code you want to shorten | move to XML: add the entry first, build and verify, then delete the attribute |

Migration is additive, so it can be done in that order without a window where the description disappears.
Removing an attribute is safe once the resource names the type or one of its members — that is what keeps the
type in the tree.

`Assets/VeloxDev.Core.agentcontext.xml` is the framework's own resource, migrated from the old attributes; it
is a worked example of every rule above at scale.

## Verify

- Build the project. Resource diagnostics point into the XML file.
- From the tool surface, `GetComponentContext(fullTypeName, language)` renders the same block for either form;
  `ListCreatableTypes` / `CreateNode` see a type that only the resource documents.
- From code, `AgentContextCollector.GetClassContext(type, language)` or `AgentContextReader.GetContexts(...)`
  returns the same texts.

## Boundaries

| Situation | What happens |
|---|---|
| `VELOX_AI_TREE002` | the resource cannot be read — malformed XML, missing root, or a missing `name` |
| `VELOX_AI_TREE003` | the named type is not in the tree — typo, another assembly, static/private/generic type, or an empty `<Type>` |
| `VELOX_AI_TREE004` | the named member is not in the tree — typo, an ignored member, or an inherited member written under the derived type |
| `VELOX_AI_TREE005` | the language name is not an `AgentLanguages` member |
| `VELOX_AI_TREE006` | an unknown element or attribute; a misspelled `language` or `name` is reported rather than silently ignored |
| `[AgentContext]` on a static/private/generic type, or on a member the generator ignores | no tree entry; the resource reports `003`/`004` instead of dropping the text |
