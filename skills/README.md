# Skills

Claude Code skills this repository ships. The seven developer skills are for developers using the
library; `veloxdev-release/` is a maintainer-only skill, documented separately below. All of them are
part of the repository, not of anyone's local configuration — the copies under a machine's
`.claude/skills/` are installs of these.

## What these skills are for

Every skill here answers one question: **how do I write my own project's code against VeloxDev, and write it the way the library intends?**

That means two things, in every skill:

- **the API is used as it is meant to be used** — the rules that decide whether the code works at all, rather than an inventory of what exists;
- **customization happens at the intended seam** — where each extension point is, and the shortcut to avoid, because almost every extension point in this library can also be reached the wrong way and still compile.

They are written for a developer who has added a NuGet package or cloned the repository, not for someone maintaining the repository itself.

The `veloxdev-create-workflow` skill's default canvas is the **whole item-template suite** — all seven roles, including the decorator (floating rulers) and the minimap. Its generated node view is the elastic `Viewbox`/layout-only container; extend that container rather than replacing it with a fixed or clipped layout.

## How a skill is organised

A skill has one `SKILL.md` and, when it needs depth, a `references/` directory with two sides:

```text
<skill-name>/
├─ SKILL.md
└─ references/
   ├─ practices/     # how to do it right — the normal path
   └─ pitfalls/      # symptom → cause → fix, for failures the repository has actually hit
```

⚙ **`practices/` is the normal path.** It states rules directly, as declarative sentences, and carries the code that demonstrates them. A practice document explains what an attached property, interface or extension point means; it does not narrate what the repository currently does.

⚙ **`pitfalls/` is the diagnostic path.** It records only failures that have actually occurred and whose consequences were severe. Each entry names the **symptom**, the rule it violates, and the fix. A violation with no observed consequence is not written down.

⚙ **The SKILL.md indexes both sides.** The normal reading path goes to `practices/`; a troubleshooting table goes to `pitfalls/` by symptom. An agent that has hit an error reads the pitfall file; an agent that is writing code reads the practice file.

⚙ **The normal path stays positive.** Rules say what to do. The wrong road lives in `pitfalls/`, so a reader following the practice does not have to filter out a catalogue of mistakes.

⚙ **No line-number citations.** A skill is self-contained outside this repository: it names types, members, files and commands, and it carries the guiding code. Line numbers rot when the source moves.

## Install

Copy the skill's **whole directory** into the `.claude/skills/` of the project you are working in — the `SKILL.md` and its `references/` folder when it ships one:

```text
<your project>/.claude/skills/<skill-name>/
├─ SKILL.md
└─ references/
```

Copy it to `~/.claude/skills/` instead to have it available in every project on that machine.

The seven developer skills are written as a set, and a few link into a sibling: `veloxdev-write-viewmodels` links to `veloxdev-create-workflow`, and `veloxdev-create-workflow` links to `veloxdev-create-animation` and `veloxdev-drive-workflow-with-ai`. A skill still works alone — only those cross-links go dead — so install all seven when you can, and read a dead cross-link as "install that skill, or find the same rule in this one". The maintainer-only `veloxdev-release` is **not** part of this set and is documented separately below.

The **directory name is the skill name** — it has to match the `name:` in the file's frontmatter. Claude Code reads the frontmatter `description` and loads the skill on its own when a request matches.

A skill may ship a `references/` directory beside its `SKILL.md`. Those files are not loaded up front; the skill points at the one that fits the task, so a large subsystem can be documented without every session paying for it.

## What is here

| Skill | When it fires |
| --- | --- |
| `veloxdev-create-workflow` | Building a workflow editor — authoring Tree / Node / Slot / Link ViewModels, compiling and running a graph, arranging a graph, scaffolding all seven item-template views (floating rulers and minimap included), putting the canvas on screen on any of the seven supported GUIs, or designing an adapter and item templates for a GUI that has none |
| `veloxdev-drive-workflow-with-ai` | Letting an LLM inspect, build and run a workflow graph — the Workflow Agent tool surface, host policy gates and call budgets, `[AgentContext]` documentation, switchable skills, MCP servers, and background sub-agents |
| `veloxdev-create-animation` | Writing VeloxDev interpolation animations — which adapter package to reference, the canonical declaration layout, static-reuse versus create-and-discard, the index forms, flow animations, and building an adapter for a GUI with no official one |
| `veloxdev-switch-themes` | Runtime themes that can animate when they change — declaring a theme, `[ThemeConfig]`, `InitializeTheme()`, and switching with `SetCurrent` / `Jump` / `Transition` |
| `veloxdev-add-aspects` | Wrapping behaviour around an existing member without editing it — `[AspectOriented]`, `Aop()`, and the before / instead-of / after hooks |
| `veloxdev-write-viewmodels` | Writing ViewModels with the source generators — `[VeloxProperty]`, `[VeloxCommand]`, collection hooks, and interop with CommunityToolkit / Prism / ReactiveUI / Caliburn |
| `veloxdev-tick-loop` | Putting a frame loop on any class — `[Tickable]`, `InitializeTickable()`, the `Awake` / `Start` / `Update` / `LateUpdate` / `FixedUpdate` hooks, channels, and `TickManager` |

## Skills with references

| Skill | Practices | Pitfalls |
| --- | --- | --- |
| `veloxdev-create-workflow` | model, canvas math, execution, view layer, input, templates, auto-layout, new adapter, `gui/<gui>` × 7 | model/connections, geometry/viewport, execution, view/input, templates |
| `veloxdev-drive-workflow-with-ai` | wiring/policy, tools, skills, MCP, sub-agents, framework providers, Agent layout | wiring, permissions/budget, MCP, skills/sub-agents |
| `veloxdev-create-animation` | declarations, segments/effects, control/samplers, flow, adapter | declarations, segments, samplers, flow, adapter |
| `veloxdev-switch-themes` | declaring, switching, converters/platforms | theme |
| `veloxdev-add-aspects` | marking/proxies, hooks | aspects |
| `veloxdev-tick-loop` | lifecycle, channels/manager | tick |
| `veloxdev-write-viewmodels` | properties, commands, interop/diagnostics | MVVM |

## Maintainer skill

`veloxdev-release/` is **not** one of the seven developer skills. It cuts this repository's own
NuGet release, and it is self-contained and unrelated to the others. Its frontmatter sets
`disable-model-invocation: true`: it may only be run when the maintainer explicitly asks, never
automatically and never from another skill. Do not install it into a consuming project.

The copy under `.claude/skills/veloxdev-release/` is the local install of this source; keep the
`skills/` copy as the versioned one. Its only reference is `references/package-plan.md`.

## Keeping them accurate

A skill that names a file, a type or a demo that no longer exists is worse than no skill: the agent will follow it and produce code that does not compile. Four habits keep this honest:

- **A skill's code examples use real repository types.** If an example names a type, that type exists — otherwise a reader cannot go and look at it, and the example teaches a shape the library does not have.
- **…and the reader can compile them.** A type that lives only inside `Examples/` as `internal` is a real repository type but not one a consuming project can name. Show the *shape* the demo demonstrates, never the demo's own scaffolding class.
- **A convention claimed as the repository's is one the repository keeps.** "Every rule below is a convention this repository already follows" is a checkable claim — check it against `Examples/`.
- **A skill's file references are checked against the tree.** Demos get moved, deleted and renamed. `Examples/` is the most volatile part of the repository, so those references are the ones that rot first.
- **Code and commit history are the baseline, and the newer commit wins.** When a skill and the source disagree, the source as of the newest commit is the fact.
