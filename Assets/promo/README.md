# VeloxDev promo films

One fourteen-second overview, one fourteen-second core film and seven
nine-second skill films, in one visual system: warm technical paper, ink type,
hairline rules and one flat accent per module. The overview opens on a real
VeloxDev editor screenshot, then cycles the seven GUIs; every effect beat is a
Scribalix-drawn running surface, not a label diagram.

| Film | Role |
| --- | --- |
| `00-overview.gif` | Repository introduction: a real node editor, seven GUIs, the pipeline, the four layers, the eight Claude skills. |
| `01-workflow.gif` | The core: the model, the node card and channels, the editor commands, the compiler and runtime. |
| `02-viewmodels.gif` … `08-cli.gif` | One Claude skill each. |

## Format

- **1280 × 720**, **15 fps**, GIF.
- **85% speed:** every film is authored on 2 s beats, then played at
  `Pace = 0.85` — 2.353 s per beat.
- Overview: **14.14 s**, six beats — real editor, seven GUIs, pipeline, layers,
  skills, close.
- Core film: **14.14 s**, six beats — intro, model, key line, effect, runtime,
  effect.
- Skill film: **9.47 s**, four beats — **intro → declaration → key line →
  effect**.
- Every effect beat is drawn by Scribalix as a running surface: a node canvas,
  a generated inspector, a moving card, three app windows, a physics square, a
  call log, an agent session or a CLI state tree.
- Every snippet is real VeloxDev API: the type, member and attribute names match
  the source. The highlighted line is the line that carries the feature.
- The code panel accepts at most eight lines at the current type size; every
  film stays inside that limit.

## Films

| Film | Claude skill | What it shows | Package |
| --- | --- | --- | --- |
| `00-overview.gif` | — | A real editor screenshot, the seven-GUI carousel, the interactive/headless pipeline, the four layers (Core, Extension, Adapters, Templates), the eight skills. | `VeloxDev.Core` |
| `01-workflow.gif` | `veloxdev-create-workflow` | `[WorkflowBuilder.Node<BiasNodeHelper>]` and `[VeloxProperty]` slots → the node card, `OneSource` / `MultipleTargets`, the editor commands → `CompileRole.Root` and `RuntimeEngine` → chain, branch, parallel, terminal. | `VeloxDev.Core` |
| `02-viewmodels.gif` | `veloxdev-write-viewmodels` | `[VeloxProperty]` → the generated property and `(oldValue, newValue)` callback; `[VeloxCommand] Task<int> AddAsync` → the typed `AddCommand` with a result channel. | `VeloxDev.Core` |
| `03-animation.gif` | `veloxdev-create-animation` | `Transition<T>.Create().Property(...).Effect(...)` → a card moves and fades on `Eases.Cubic.Out`, with the curve beside it. | `VeloxDev.<GUI>` |
| `04-themes.gif` | `veloxdev-switch-themes` | `[ThemeConfig<BrushConverter, Light, Dark>]` and `InitializeTheme()` → Light, Dark and a runtime override. | `VeloxDev.<GUI>` |
| `05-tick.gif` | `veloxdev-tick-loop` | `[Tickable("physics", TargetFPS = 60)]` and the five `partial void` hooks → a square moving on the fixed step. | `VeloxDev.Core` |
| `06-aspects.gif` | `veloxdev-add-aspects` | `[AspectOriented]` + `service.Aop().SetProxy(...)` → caller → proxy → target, with before, coverage and after. | `VeloxDev.Core` |
| `07-ai.gif` | `veloxdev-drive-workflow-with-ai` | `tree.AsAgentScope()` → prompt → tool → policy → graph, with MCP, skills, sub-agents and the permission modes. | `VeloxDev.Core.Extension` |
| `08-cli.gif` | `veloxdev-use-workflow-cli` | `veloxdev mcp` / `veloxdev run` → the archive and its assemblies, an isolated worker, and the whole headless surface. | `VeloxDev.Cli` |

## Beats

Playback times, after the 85% slowdown:

| Beat | Core film | Skill film | Content |
| --- | --- | --- | --- |
| 1 | 0.00 – 2.35 s | 0.00 – 2.35 s | Intro: the module and the Claude skill it teaches |
| 2 | 2.35 – 4.71 s | 2.35 – 4.71 s | The declaration |
| 3 | 4.71 – 7.06 s | 4.71 – 7.06 s | The line that matters |
| 4 | 7.06 – 9.41 s | 7.06 – 9.41 s | The effect |
| 5 | 9.41 – 11.76 s | — | The runtime |
| 6 | 11.76 – 14.12 s | — | The compiled plan |

The overview uses six beats: real editor, seven GUIs, pipeline, layers, skills,
close.

## Verification

Regenerate from the Scribalix working tree:

```powershell
cd E:\VisualStudio\Projects\Scribalix
dotnet run --project tools/veloxdev-promo -- --gif-only --out ..\VeloxDev\Assets\promo
```

Inspect a film:

```powershell
ffprobe -v error -select_streams v:0 `
  -show_entries stream=width,height,r_frame_rate,nb_frames,duration `
  -of default=noprint_wrappers=1 Assets\promo\01-workflow.gif
```

Expected and verified on 2026-11-07:

| Film | Resolution | Frame rate | Frames | Duration |
| --- | --- | --- | --- | --- |
| `00-overview.gif` | 1280 × 720 | 15/1 | 212 | 14.140 s |
| `01-workflow.gif` | 1280 × 720 | 15/1 | 212 | 14.140 s |
| `02-viewmodels.gif` … `08-cli.gif` | 1280 × 720 | 15/1 | 142 | 9.470 s |

The exporter reports 14.112 s / 9.408 s; the GIF container is 14.140 s /
9.470 s because frame delays are quantised to centiseconds.

Each film is also a VeloxDev workflow archive (`<film>.json`). Load and run one
through the CLI to confirm it is a real graph:

```powershell
veloxdev run --archive Assets\promo\02-viewmodels.json `
  --assembly ..\Scribalix\src\Scribalix.Graph\bin\Release\net10.0\Scribalix.Graph.dll `
  --root-type Scribalix.Graph.WorkflowTreeViewModel --start 0
```

All nine archives were run with this command on 2026-11-07; each reported
`outcome: Completed` with no failures.

The film sources live in the Scribalix repository:

- `tools/veloxdev-promo/PromoScenes.cs` — the overview, the core film and the seven skill films.
- `tools/veloxdev-promo/PromoFilm.cs` — the paper scene kit, code panel, highlight bar, real UI shots, the 85% pace and the poster-frame rise.
- `tools/veloxdev-promo/Program.cs` — archive and GIF output.
- `assets/promo/veloxdev-studio.png` — the real editor screenshot used by the overview.

[Back to the repository README](../../README.md)
