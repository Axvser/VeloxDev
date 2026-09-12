# Running the acceptance suite — a working guide

For an agent asked to "run the AT" / "跑一次验收" / "check the transition demos". Everything here was learned by running
it; the numbers are measured, not estimated.

**Audience:** an agent or engineer who has to get a verdict out of this project with as few wasted runs as possible.
For what the suite *checks*, read [README.md](README.md); this file is about operating it.

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
  can reach it. The tokens for each family are listed in README.md.
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

### Known intermittent: `... is still outside the surface after being brought into view`

**This one is not a product defect, and it is the failure most likely to be misread as one.** It looks like this:

```
…the handle 'over.sampler.BrushSampler' is still outside the surface after being brought into view
   (control 0,0 0x0, window 228,228 1280x745, IsOffscreen=True), so a person could not reach it.
```

`0x0` at `0,0` means the element exists but reports no rectangle — on WinUI a row scrolled out of the viewport does
that. The reachability check is right to flag it; what it cannot distinguish is "the demo really has an unreachable
row" from "the scroll request did not take effect this time".

Measured: in 8 full runs after the check was made to re-issue its scroll request while polling, 1 failed this way; the
same platform (`WinUI`) passes 4/4 when run on its own. It has never been seen on any other platform, and it is load
sensitive — it has only ever appeared in a full seven-platform run.

What to do: **re-run once before investigating.** If it recurs on the same platform and the same row, treat it as real
and look at that demo's layout — the check earned its place by catching a WinUI row that was genuinely laid out
off-window. If it moves around or does not recur, it is the flake.

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
- **The wall-clock table in README.md is partly stale.** Only the fast row has been measured since the restructure.
