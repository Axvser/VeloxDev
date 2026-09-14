# Skills

Claude Code skills this repository ships **for developers using the library**. They are part of the repository, not of anyone's local configuration — the copies under a machine's `.claude/skills/` are installs of these.

## What these skills are for

Every skill here answers one question: **how do I write my own project's code against VeloxDev, and write it the way the library intends?**

That means two things, in every skill:

- **the API is used as it is meant to be used** — the rules that decide whether the code works at all, rather than an inventory of what exists;
- **customization happens at the intended seam** — where each extension point is, and the shortcut to avoid, because almost every extension point in this library can also be reached the wrong way and still compile.

They are written for a developer who has added a NuGet package or cloned the repository, not for someone maintaining the repository itself. How the adapters are built, how the demos are maintained and how a release is cut are deliberately out of scope.

## Install

Copy the skill's directory into the `.claude/skills/` of the project you are working in:

```
<your project>/.claude/skills/<skill-name>/SKILL.md
```

Copy it to `~/.claude/skills/` instead to have it available in every project on that machine.

The **directory name is the skill name** — it has to match the `name:` in the file's frontmatter. Nothing else is needed: Claude Code reads the frontmatter `description` and loads the skill on its own when a request matches.

A skill may ship a `references/` directory beside its `SKILL.md`. Those files are not loaded up front; the skill points at the one that fits the task, so a large subsystem can be documented without every session paying for it.

## What is here

| Skill | When it fires |
|---|---|
| `veloxdev-create-workflow` | Building a workflow editor — authoring Tree / Node / Slot / Link ViewModels, compiling and running a graph, putting the canvas on screen on any of the seven supported GUIs, or designing an adapter and item templates for a GUI that has none |
| `veloxdev-drive-workflow-with-ai` | Letting an LLM inspect, build and run a workflow graph — the Workflow Agent tool surface, host policy gates and call budgets, `[AgentContext]` documentation, and MCP servers |
| `veloxdev-create-animation` | Writing VeloxDev interpolation animations — which adapter package to reference, the canonical declaration layout, static-reuse versus create-and-discard, the index forms, and building an adapter for a GUI with no official one |
| `veloxdev-switch-themes` | Runtime themes that can animate when they change — declaring a theme, `[ThemeConfig]`, `InitializeTheme()`, and switching with `SetCurrent` / `Jump` / `Transition` |
| `veloxdev-add-aspects` | Wrapping behaviour around an existing member without editing it — `[AspectOriented]`, `Aop()`, and the before / instead-of / after hooks |
| `veloxdev-write-viewmodels` | Writing ViewModels with the source generators — `[VeloxProperty]`, `[VeloxCommand]`, collection hooks, interop with CommunityToolkit / Prism / ReactiveUI / Caliburn, and the `[MonoBehaviour]` frame loop |

## Skills with references

| Skill | References |
|---|---|
| `veloxdev-create-workflow` | `references/model.md` (Tree/Node/Slot/Link, helpers, undo, serialization) · `canvas-math.md` (the complete coordinate model) · `execution.md` (the compiler and runtime engine) · `view-layer.md` (what any GUI must supply) · `templates.md` (the `dotnet new` packs) · `new-adapter.md` (supporting an unsupported GUI) · `gui/<gui>.md` × 7 (per-GUI adapter and template detail) |
| `veloxdev-drive-workflow-with-ai` | `references/tools.md` (the tool surface, by category) · `references/mcp.md` (MCP configuration and lifecycle) |
| `veloxdev-create-animation` | `references/adapter.md` (the transition-system adapter contract) |

## Keeping them accurate

A skill that names a file, a type or a demo that no longer exists is worse than no skill: the agent will follow it and produce code that does not compile. Four habits keep this honest:

- **A skill's code examples use real repository types.** If an example names a type, that type should exist — otherwise a reader cannot go and look at it, and the example teaches a shape the library does not have.
- **…and the reader can compile them.** A type that lives only inside `Examples/` as `internal` is a real repository type but not one a consuming project can name. Show the *shape* the demo demonstrates, never the demo's own scaffolding class.
- **A convention claimed as the repository's is one the repository keeps.** "Every rule below is a convention this repository already follows" is a checkable claim — check it against `Examples/`. If the demos do not do it, the rule is a recommendation and has to say so.
- **A skill's file references are checked against the tree.** Demos get moved, deleted and renamed. `Examples/` is the most volatile part of the repository, so those references are the ones that rot first.
