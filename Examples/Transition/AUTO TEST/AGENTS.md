# Running the acceptance suite — a working guide

For an agent asked to "run the AT" / "跑一次验收" / "check the transition demos". Everything here was learned by running
it; the numbers are measured, not estimated.

**Audience:** an agent or engineer who has to get a verdict out of this project with as few wasted runs as possible.
This file is the only one for this project: how to operate it, what it checks, and how to extend it.

It is **not in `VeloxDev.slnx`**, deliberately. The solution is everything `dotnet test` at the repository root
resolves, so being absent from it is what keeps that command backend-only — a guarantee rather than a filter. Run this
project **by path**, as every command below does.

---

## 1. The 90-second version

```bash
# from the repository root — build the demos FIRST, in exactly this form (see §4.1)
for p in WPF WinForms Avalonia MAUI WinUI Jalium; do dotnet build "Examples/Transition/$p/Demo/Demo.csproj" -c Debug; done
dotnet build "Examples/Transition/Blazor/Demo/Demo/Demo.csproj" -c Debug

# then run. Budget 5+ minutes of tool timeout even for the fast form.
cd "Examples/Transition/AUTO TEST"
VELOXDEV_AT=1 VELOXDEV_AT_PACE=0 VELOXDEV_AT_OBSERVE=0 VELOXDEV_BENCH_MS=200 \
  dotnet test VeloxDev.AT.csproj --nologo
```

Measured: **32 tests, all 7 GUIs, ~1m30s.** Add `--logger "console;verbosity=normal"` if you want to see the order.

If you forget `VELOXDEV_AT=1` **every test reports as skipped and the run exits 0**. That is the single most common way
to get a confident wrong answer out of this project.

---

## 2. What one run looks like from the outside

Since the restructure, the run is **platform-major**: for each GUI the window opens once, every aspect is driven through
it, and it closes before the next one opens.

```
guards (no GUI)                          ~0s
[WPF]      probe → load modes → samplers → timeline control   → window closes
[Avalonia] same four                                          → window closes
... 7 platforms ...
[Blazor]   same four + one browser-only check                 → page closes
```

Verified by sampling the OS process table once a second across a full run: **the peak number of live `Demo.exe`
processes is 1.** If you ever see more than one, something has broken the contract in §5.

---

## 3. Reading the result

- **The verdict line is the only thing that matters**: `已通过! - 失败: 0，通过: 32` (or `失败!` with a count). Exit code
  0 means green.
- **`[AT] <Platform> · <case>` lines** are printed by `driver.Show(...)` as each case starts. With
  `VELOXDEV_AT_OBSERVE=0` they still print — that is your live progress indicator, and the fastest way to see *which*
  case a hang is stuck in.
- **Assertion messages name the platform and the field.** A conformance failure says which sampler and which time index;
  a timeline failure says which operation and what the payload read back. You usually do not need to re-run to know what
  broke.

---

## 4. Traps that have actually cost time here

### 4.1 A stale demo binary looks exactly like a code bug

The suites **start the demos' executables**; they do not reference their projects. A demo that failed to build leaves
the previous `.exe` in place and the suite runs happily against it. The failure then looks like a product bug.

Worst instance seen: `dotnet build ... -p:Platform=x64` on the WinUI demo writes to
`bin/x64/Debug/.../win-x64/`, but the driver expects `bin/Debug/.../win-x64/`. The suite then ran a months-old binary
and reported `The payload has no 'pos'` — a "missing feature" that was in fact a missing *build*.

**Rule: build with the exact command in §1 (no extra `-p:Platform`), and rebuild after any edit you revert.**

### 4.2 Filter syntax

```bash
--filter "TestCategory=AT.WPF"     # correct
--filter "TestCategory~AT.WPF"     # also works
--filter "Category=AT.WPF"         # matches nothing, reports success
```

The third form silently green-lights a run over zero tests.

### 4.3 Naming and payload ordering

- The suites locate controls **by automation id only** — never by label. Adding a button without a token means no test
  can reach it. The tokens, by family: `over.btn.*` (toolbar — `start.all` / `stop.all` / `reset.all`, the five
  `load.*` modes, and the six timeline controls `pause` / `resume` / `rate.slow` / `rate.fast` / `rate.normal` /
  `seek.next`), `over.sampler.<SamplerTypeName>` (a case row's start, which is what AT clicks),
  `over.row.{stop,reset}.<id>` (that row's other two), and `over.{state,conf,live,batch,bench}` for the
  payloads. **The payloads are in the tree but not drawn** — every demo keeps them invisible rather than hidden,
  because a hidden element leaves the automation tree and `over.state` is what every driver's readiness handshake
  reads. `over.readout`, a human-readable restatement of the same values, is gone.
- `over.state` is a flat `k=v;` payload and **`nomutual=` must stay the last field** (there are comments at each
  builder saying so). Insert new fields *before* it.
- `rate=` must be formatted with `CultureInfo.InvariantCulture`. The current-culture default turns `0.25` into `0,25`
  on some machines and the parser fails to read it.

---

## 5. The contract you must not break

Each platform is **one test class** (`Suites/PlatformSuites.cs`) that opens its demo in `[ClassInitialize]` and closes it
in `[ClassCleanup]`. That is what keeps one window on screen at a time.

- Adding a platform = one class there + one line in `DemoCatalog.Registry`.
- Adding an aspect = a check method in `Suites/Checks/*` plus one `[TestMethod]` per platform class.
- **A suite class that drives a demo and does not open/close it fails the build's test run** — `CoverageSuite` reflects
  over the assembly and fails on exactly that. It also fails if a platform has a table but no case, or is driven but has
  no table.

Do not "fix" a slow run by putting several platforms back into one class: that is the arrangement that made the run
unwatchable, and it is what the per-platform structure replaced.

---

## 5b. What each aspect actually checks

Per platform, four test methods in the platform's suite class, in this order:

| Method | What it proves |
|---|---|
| `ObservationSurface_IsReachableAndTicking` | The launch path, the automation tree and the payload's spelling are all sound, and the readout's sequence number advances on its own. **When this fails, everything below it on that platform is downstream — fix it first.** |
| `LoadModes_MatchTheLibrarySemantics` | How a load is started: mutual vs concurrent, UI thread vs background. The load-bearing observable is `nomutual=` in `over.state`. |
| `EverySamplerMatchesItsClosedForm` | Two halves, from one click: the arithmetic (one frame per sampler per eased time, on `over.conf`, against a closed form written independently) and the junction (a real transition run on the live control, published on `over.live`). The second half exists because the sampling loop swallows whatever a sampler throws, so a sampler producing a value the framework rejects leaves no other trace than an element frozen short of its target. |
| `TimelineControl_SteersTheRunningAnimation` | Pause freezes the position and keeps it frozen, resume advances it, a quarter speed covers visibly less of the same wall clock, and a jump to another pass moves `cycle`. It deliberately does **not** assert the arithmetic — the pass-local seek and the exclusion of paused time are pinned by the unit tests, which can measure them precisely. |

Blazor has a fifth, `SamplerBench_PaintsTheColourTheSamplerProduced`, which reads the browser's own computed style
rather than the app's payload — the most literal form of "verified through the real UI" available anywhere here.

A sampler whose product is a framework object needs a live runtime even to build its endpoints, so the only place it
can be driven is an app that already has one. That is the whole reason this project exists as well as the pure-data
suite next to it under `Samplers/`, which needs no desktop and runs anywhere.

## 6. Tuning the pace

| Variable | Default | What it does |
|---|---|---|
| `VELOXDEV_AT_PACE` | 800 ms | pause after **every click**. Raise for a watchable run, `0` for a verdict. |
| `VELOXDEV_AT_OBSERVE` | 1200 ms | pause at the start of **every case**, after its banner. `0` for a verdict. |
| `VELOXDEV_BENCH_MS` | 800 ms | how long each demo plays a case through a real transition. `200` is the fast run. |
| `VELOXDEV_AT_PLATFORMS` | all | e.g. `WPF,Avalonia` — the cheapest way to iterate on one platform. |

For a human-watchable run, leave the defaults and raise the pace:
`VELOXDEV_AT=1 VELOXDEV_BENCH_MS=1600 VELOXDEV_AT_PACE=2500 dotnet test VeloxDev.AT.csproj`

---

## 7. Iterating cheaply

```bash
# one platform, fast — the loop to use while working on a single adapter
cd "Examples/Transition/AUTO TEST"
VELOXDEV_AT=1 VELOXDEV_AT_PLATFORMS=WPF VELOXDEV_AT_PACE=0 VELOXDEV_AT_OBSERVE=0 VELOXDEV_BENCH_MS=200 \
  dotnet test VeloxDev.AT.csproj --nologo --filter "TestCategory=AT.WPF"
```

Roughly 15 seconds. Only widen to all seven once the one is green — and note that **a green single-platform run proves
nothing about the other six**: the demos are separate applications with separate automation surfaces, and platform-only
failures (a row off-screen, a token spelled differently) are exactly what this suite exists to catch.

### Solved: `... is still outside the surface after being brought into view`

Worth reading even though it is fixed, because the mechanism is the kind of thing that comes back the moment someone
adds a step that focuses a control and then scrolls the list.

**Focus is a change trigger, not a command.** All seven frameworks agree on "focus it and I will scroll it into view",
which is why the harness asks that way. But focusing an element that *already holds focus* is a silent no-op — so a
control that was focused at some earlier step and then scrolled away cannot be brought back by focusing it again, no
matter how many times the request is repeated. The failure looked like this:

```
…the handle 'over.sampler.BrushSampler' is still outside the surface after being brought into view
   (control 0,0 0x0, …, HasFocus=True, focused='over.sampler.BrushSampler'; ancestors: [Pane … scroll V=True/95.3%]…
   4 of 10 'over.sampler.*' handles are inside the window), so a person could not reach it.
```

`HasFocus=True` on a control that is `0x0` and out of view is the whole diagnosis: the element was reachable by focus
and unreachable *because* of it. The scenario's last step had left the list scrolled to the bottom with the row still
holding focus.

`BringIntoView` now tries three things in the order a person would, and the three exist because the first one cannot
always work:

1. **Focus it**, re-asking while polling rather than asking once and then only waiting — a request that did nothing is
   otherwise never retried.
2. **If it already holds focus**, that request can never work: activate the window first, to move focus off it, so the
   next request is a real change.
3. **If focus cannot do it at all**, drive the ancestor's `ScrollPattern` directly. That path does not involve focus.

There is also a short grace period before the guard declares failure, because a scroll can be an animation in flight —
treating one instantaneous read as final is how you report a failure that is not there. (One captured failure showed a
rectangle that had already come back inside the window by the time the message was built.)

Measured: the pair reproducer below failed twice in eight runs before the change and passed ten of ten after it; four
full seven-platform runs passed as well.

**Reproducing it cheaply.** Do not run all seven to chase this. It appears in a two-platform run, which costs about
25 seconds instead of 90:

```bash
VELOXDEV_AT=1 VELOXDEV_AT_PLATFORMS=Avalonia,WinUI VELOXDEV_AT_PACE=0 VELOXDEV_AT_OBSERVE=0   VELOXDEV_BENCH_MS=200 dotnet test "Examples/Transition/AUTO TEST/VeloxDev.AT.csproj" --nologo
```

And `Suites/ReachabilityStress.cs` (ignored by default) repeats the suspect sequence about fifty times in ten seconds;
remove its `[Ignore]` and run it by name when chasing something in this area.

### When a run fails

1. Look at the **platform** in the message. If it is the same platform you just edited, suspect your edit.
2. Look at the **case name**. `ObservationSurface` failing means the launch path, automation tree or payload spelling —
   not a product bug. Fix that first; every other case on that platform is downstream of it.
3. Rebuild the demo (§4.1) before believing anything else. It is the cheapest hypothesis to eliminate and the one that
   has twice been the actual answer.

---

## 8. What not to expect

- **Nothing here needs a headless CI today.** These tests drive real windows over UI Automation and a browser over
  Playwright; they need an interactive desktop. Without `VELOXDEV_AT=1` they skip.
- **The run is not parallel and cannot be.** `[assembly: DoNotParallelize]` is deliberate: one demo, one user at a
  time. Making it parallel would put several windows on screen and break the property in §2.
- **Timings are quoted where they were measured, and only there.** The fast full run is 32 tests across seven
  processes in about 1m30s (measured); the default-pace and watch-everything runs have not been re-measured since the
  restructure, and nothing in this file will pretend otherwise.
