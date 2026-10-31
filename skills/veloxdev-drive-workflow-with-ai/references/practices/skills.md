# Skills

`WithSkills(rootPath)` puts skills under dynamic management: the library's own embedded prompt documents plus every skill folder found under `rootPath`. Each becomes a bindable row in `scope.Skills.Status.Skills` that the host **and the agent** can switch on and off, and the change reaches the model on the next turn.

```csharp
scope.WithSkills("skills");    // relative paths resolve against AppContext.BaseDirectory
```

That is the whole wiring: the skill context provider contributes the corpus *and* the four tools.

## The four tools

| Tool | Effect |
|---|---|
| `ListSkills` | every discovered skill with its source (`Embedded` / `File`), state, enabled flag, resource count and error |
| `load_skill` | reads a skill's full text and switches it on |
| `UnloadSkill` | switches a skill off without unloading or rediscovering |
| `read_skill_resource` | reads one resource under the skill's `references/` tree |

⚙ **They are classified as query tools for the budget wherever they come from** — contributed by the provider, they are charged to `MaxReadToolCalls` and never mark the tree dirty, because switching a skill changes what the model is shown and never the graph. The classification is taken from `SkillAgentToolkit.ToolNames`, so it holds whether the provider or a host registered them. For **permissions**, they carry the default `AgentActionCategory.Execute`, so `Plan` denies them and `AutoEdit` asks. The budget and the permission gate read different things.

## The provider

A skill provider's render is keyed on the scope's version *and* its prompt language. `scope.WithPromptLanguage(...)` reaches `SkillScope` for a language change to take effect; a standalone host sets it with `SkillScope.WithPromptLanguage`.

A standalone skill host needs no workflow tree:

```csharp
var skills = new SkillScope().WithSkillRoot("skills");
skills.Refresh();
var agent = chatClient.AsAIAgent([skills.CreateContextProvider()]);
```

## Two injection shapes

| Origin | What the model sees |
|---|---|
| Embedded documents | Architectural prompt text: when enabled, the body is injected **in full**. |
| Disk skills | The Agent Skills convention: only `name` and `description` are advertised; the body arrives through `load_skill`. |

⚙ **With skills attached before the skeleton renders, the static prompt omits the corpus.** `ProvideProgressiveContextPrompt()` leaves it to the provider; leaving it in both sends every document twice. Attaching skills *after* that string was built leaves the corpus frozen in: the provider sees it was already delivered and contributes only the documents withdrawn since.

## The disk format

A disk skill is `<root>/<name>/SKILL.md` with `---` frontmatter carrying `name` and `description` — the same layout as a Claude Skill.

- The directory name equals the frontmatter `name`.
- The name is lowercase kebab-case, at most 64 characters.
- The description is at most 1024 characters.
- A `references/` folder beside the `SKILL.md` ships extra documents, counted but not read until `read_skill_resource` asks for one. Resource paths are confined to the skill directory.

⚙ **A malformed skill is reported, not swallowed and not fatal.** It appears in the list with `State == Error`, an `Error` message, and `IsActive == false`, so one broken folder cannot take the others down or silently vanish. Check `scope.Skills.Status.ErrorCount`.

⚙ **All skills start enabled**, so adding a root never silently empties the prompt. `Refresh()` rediscovers and preserves the flags — call it when a root's contents changed; do not call it per turn, since it always advances `Skills.Version` and invalidates the provider's render.

## The status model

`scope.Skills.Status` is a `SkillsViewModel`: the row list (`Skills`), `ActiveCount`, `ErrorCount`, and `Snapshot` — an immutable copy republished on the bound thread. Read `Skills` from the UI; read `Snapshot` from the agent invocation thread or any other thread.

When a skill does not appear or will not switch, read [references/pitfalls/skills-and-subagents.md](../pitfalls/skills-and-subagents.md).
