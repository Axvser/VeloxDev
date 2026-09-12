# VeloxDev.AT — sampler conformance, in a real app

Drives each transition demo and checks, **inside the running application**, that every sampler the platform's adapter
ships produces exactly what an independently written closed form says — and that the value the real animation pipeline
leaves on the element holds up while it runs.

It answers two questions a headless suite cannot. A sampler whose product is a framework object — a brush, a shadow, a
transform, a projection — needs a live runtime even to build its endpoints, so the only place it can be driven is an
app that already has one. And an animation that dies halfway leaves no trace: the pipeline swallows whatever a sampler
throws, so the only evidence is what the element is left holding. The demo does the driving; this project holds the
expectation.

It is the outer half of a pair. `Samplers/` next to it is the inner half: a pure-data suite that verifies every sampler
it can construct without a runtime, and which needs no desktop and can run anywhere.

## Why this project is not in `VeloxDev.slnx`

Deliberately absent. The solution is all `dotnet test` at the repository root resolves, so not being in it is what
keeps the root command backend-only — a guarantee, not a filter. Run this project by path.

## Running it

Build the demos first; these suites start their executables, they do not reference their projects (a `ProjectReference`
would drag the MAUI and WinUI workloads into a project that only ever talks to them over UI Automation).

**Check that the demo build actually succeeded, and rebuild after any edit you revert.** A failed build leaves the
previous executable in place, and these suites run whatever is there — so a demo that does not compile shows up as a
green acceptance run over a stale binary. The mirror image bites too: sabotage a handler to check that a test really
catches it, restore the file, and forget to rebuild, and every later observation is made against the sabotaged
executable while the source reads correct. Both have happened here.

```bash
for p in WPF WinForms Avalonia MAUI WinUI Jalium; do dotnet build "Examples/Transition/$p/Demo/Demo.csproj" -c Debug; done
dotnet build "Examples/Transition/Blazor/Demo/Demo/Demo.csproj" -c Debug

VELOXDEV_AT=1 dotnet test "Examples/Transition/AUTO TEST/VeloxDev.AT.csproj"                     # every platform
VELOXDEV_AT=1 dotnet test "Examples/Transition/AUTO TEST/VeloxDev.AT.csproj" --filter "TestCategory=AT.WPF"
```

`--filter "TestCategory=AT.WPF"` is the form this runner accepts. `TestCategory~AT.WPF` also selects correctly, but
`Category=AT.WPF` does **not** — that name is not recognised and the run reports no matching tests.

Without `VELOXDEV_AT=1` every test is reported as skipped and the run still exits 0.

## Environment variables

| Variable | Meaning |
|---|---|
| `VELOXDEV_AT` | `1` lets the suites run. Unset means they skip. |
| `VELOXDEV_AT_PLATFORMS` | Comma-separated subset, e.g. `WPF,Blazor`. Unset means all. |
| `VELOXDEV_AT_DEMO_ROOT` | Directory to resolve demo executables under, instead of the repository root. Point it at a publish output. |
| `VELOXDEV_AT_PACE` | Milliseconds to linger after **every** click a suite makes — sampler handles and load-mode buttons alike. **Defaults to 800** so a run can be watched; `2500` is a comfortable pace for watching the whole thing, `0` goes straight on to reading the payloads. |
| `VELOXDEV_AT_OBSERVE` | Milliseconds to hold at the start of **each case**, after the banner naming it is printed. **Defaults to 1200**; `0` moves straight on. Separate from the pace because the two answer different questions — see below. |
| `VELOXDEV_BENCH_MS` | How long the demo plays each case through a real transition. **Defaults to 800.** A bulk run is only finished once its slowest row has settled, so this sets the floor for the sampler suites — `200` is the fast run, and the demos still visibly animate at that length. |

The pause lives in `DemoDriverBase.Click`, not in the suites — every click goes through that one method, so a case
added later cannot quietly leave it out. It was inside `ActivateSampler` alone until it turned out that left the
load-mode row (a dozen clicks a platform, each starting real animations) flashing past unobservably.

The other two pauses are one level up. `DemoDriverBase.Show` names the case about to run and holds the surface for
`VELOXDEV_AT_OBSERVE`, so a viewer can read what is being checked and see what the last case left behind; it is called
from each suite's case loop. And the demo itself is started **once per platform for the whole run** rather than once
per suite — the window no longer vanishes and reappears between cases, which was the most disruptive thing to watch
and also the largest single cost in the run.

### One demo per platform, not one per suite

`DemoCatalog.For(platform)` launches on first use and keeps the process until the assembly teardown. Seventeen
launches across the four suites became seven, one per platform.

The contract that comes with it: **a suite no longer starts from a fresh process**, so it has to bring the demo to a
known state itself. That is what `IDemoDriver.Settle()` is — stop everything, then reset — called at the top of each
case. This is not a new obligation so much as a stated one: every suite already reset before it measured, so a case
can be checked by asking whether it still does.

Two consequences worth knowing:

- **Suite order no longer matters, but state does.** `DemoCatalog.For` is a plain dictionary, which is only safe
  because `AssemblyInfo` already marks the whole assembly `DoNotParallelize` — one case runs at a time, so a shared
  driver never has two users.
- **Blazor gets the most out of this.** Its host binds a fixed port and refuses to start while anything answers on it,
  so four sequential servers were also four chances to trip over a stale one. There is now one.

### How long it takes, measured

> The first and third rows describe the state **before** demos were shared per platform and before the timeline-control
> row and suite existed. The launch count went from seventeen to seven, an eighth test method per platform was added,
> and there is now a second pause (`VELOXDEV_AT_OBSERVE`). Re-measure them before quoting; only the fast row below has
> been measured since.

| Invocation | Wall clock | What dominates it |
|---|---|---|
| `VELOXDEV_AT=1 dotnet test …` (defaults — watchable) | 2m48s | the load-mode suites: ~98 clicks × the 800 ms pace |
| `VELOXDEV_BENCH_MS=200 VELOXDEV_AT_PACE=0 VELOXDEV_AT_OBSERVE=0 VELOXDEV_AT=1 dotnet test …` | **1m32s, 27 tests** | the seven remaining demo launches, plus Blazor's server |
| `VELOXDEV_BENCH_MS=1600 VELOXDEV_AT_PACE=2500 VELOXDEV_AT=1 dotnet test …` (watch everything) | 5m54s | the pace, plus 59 × 1.6 s of animation |

Measured for the fast row: seven processes for 27 tests, against seventeen processes for 19 before — so the launch
count is no longer what the fast run is made of, and adding the timeline suite cost about ten seconds rather than the
four launches it would have cost.

Two things worth knowing about those numbers:

- **The bulk channel is what made the sampler suites cheap.** Before it, each of the 59 samplers cost a click, a whole
  animation and its own settle window; now it costs one animation as part of a batch, and the settle window is paid
  once per platform rather than once per sampler. That is why the fast run went from 1m43s to 1m22s and the watchable
  one from 7m36s to 5m54s, with the same coverage.
- **What dominates the default run is pacing the load-mode suites**: ~98 clicks × the 800 ms pace ≈ 75 s. Those clicks
  went through the driver without a pause until it turned out that left a row of real animations flashing past
  unobservably. `VELOXDEV_AT_PACE=0` removes all of it.

The floor was the demo launches — one per suite per platform — not the animations. Lowering `VELOXDEV_BENCH_MS`
further buys very little; sharing the process is what buys the most.

## What is checked

Each demo presents its cases as a **list of rows**: one row per sampler the adapter ships, plus rows for the load-mode
and overshoot scenarios. A row carries the element that case really animates, a sentence saying what it verifies, and
its own `启动 / 关闭 / 重置`. Above the list sits the toolbar: the global trio (`全部启动 / 停止全部 / 重置`), the five
loading modes, and the six timeline controls (`暂停 / 恢复 / 慢速 / 快速 / 正常 / 下一程`) — they stay there because they
are *how* to load or steer, not *what* to load, which is what lets every row have the same three controls. All of it is
generated from one table per demo, so a case is added in exactly one place.

The timeline row acts on the three long animations the loading modes drive, not on the overshoot rows: pausing one of
those is indistinguishable on screen from it simply being quick. Its state comes back in `over.state` as
`paused` / `rate` / `pos` / `cycle`, read from the first of the three, so `TimelineControlSuite` asserts that a run
really froze, really resumed, and really moved to the pass it was told to. What it does *not* assert is the arithmetic
— the pass-local seek, the rewind-to-start rule and the exclusion of paused time are pinned by the unit tests, which
can measure them precisely; this suite exists to prove the whole path works on a real surface.

A sampler row's 启动 carries the token `over.sampler.<SamplerTypeName>` and does what a click has always done. It is
now clicked **once per platform** as a spot check, because driving every row that way costs one click and one whole
animation apiece. The rest goes through the bulk channel.

```text
v=1;seq=<activation count>;n=5;s.<SamplerTypeName>.<time index>=<TypeName>,<component>,<component>,…;…
```

- Clicking a handle runs **only that sampler**, at all five eased times. The verification therefore goes through the
  real UI path — click → handler → sampler writes → read back — rather than an app reporting what it computed at
  startup.
- The sampler writes onto **a real on-screen control's dependency properties** — one per sampler, same type as the
  product — and the payload reads the value back **from that control**. That is what makes the assertion about what
  the UI holds rather than about a scratch object: WPF and WinUI use `DependencyProperty` with the framework's own
  change notification, WinForms calls `Invalidate()` from its setters, MAUI has no `AffectsRender` counterpart at all
  (dropping the call does not error — the canvas simply freezes on its last frame). The control draws itself from
  those properties, so there is no separate mapping code to keep in step.
- **Blazor is the exception, and the most literal reading of the same idea**: a browser has no control property to
  read, so its real appearance *is* its computed style. `Blazor_SamplerBench_PaintsTheColourTheSamplerProduced` reads
  the browser's own `background-color` and compares it against the sampler's declared endpoint colour — an expectation
  taken from the closed form, not from the payload.
- `seq` increments once per click and is what the suite waits on. Two reads whose sequence differs are the one sound
  proof that the click landed rather than being dropped, and it is not replaceable by a sleep.
- The time grid is `[0, 0.5, 1, 1.5, -0.5]` — the same one the pure-data suite uses, so a failure on either side names
  the same time. All five are exactly representable in binary, so the grid itself adds no error.
- A destination is driven through the same property path a real animation would use, and each frame gets a **freshly
  constructed target and a fresh pair of endpoints**: the library requires that a sampler never mutate the `start` and
  `end` it is handed, and per-frame endpoints are what make a violation of that visible.
- The value's type name leads the components. A sampler that starts producing something else is reported as that
  rather than as a pile of component mismatches.

The expectation lives in `Conformance/<Platform>Conformance.cs`, written from the rule and referencing **no product
adapter assembly** — `VeloxDev.AT` has zero workload dependencies, and nothing here can be answered by the sampler
under test. The comparison is numeric with a small tolerance, because the two sides are separately compiled copies of
the same arithmetic and the last bit may differ.

### The bulk channel: one click for every row

The per-row payloads are shaped around "click one row, read what it wrote", so verifying twelve rows that way costs
twelve clicks and twelve waits — and each wait is a whole animation. `over.batch`, written by 全部启动 and read once,
carries the same two things for **every** row at once, in the same field shapes the per-row payloads use (which is why
the parsers are reused rather than duplicated):

```text
v=1;seq=<n>;done=<0|1>;rows=<n>;
s.<Sampler>.<time index>=<TypeName>,<component>,…;      ← one row's five closed-form frames, as over.conf writes them
l.<Sampler>.type=…;….k=…;….samples=…;….bad=…;….err=…;….last=…;….min=…;….max=…;
```

`done=0` at click time and `done=1` once **every** row has settled, so the suite can tell "still running" from "the
previous run's payload" without a sleep. Each row keeps its **own** `LiveWatch` — a shared one could not say which of
twelve concurrent rows failed — and the bulk start deliberately does not go through a sampler row's own 启动, which
stops the previously-watched row and would therefore have each row kill the one before it (measured: 1 of 12 left
running).

Three more things the same click drives, all observable only through `over.state`:

| Field | Meaning |
|---|---|
| `rows=<n>` | how many sampler rows the demo has, cross-checked against the conformance table |
| `away=<n>` | rows whose current value differs from the **declared start** they were written to at load |
| `moving=<n>` | rows whose value changed since the previous readout tick |

They are inserted immediately *before* `nomutual=` — which stays last, and which the label must still render in full.
The toolbar phase then asserts, model-free: 重置 puts every row back at its start (`away == 0`), 全部启动 moves every
row that the table says should move (`away == movable`, **waited for** rather than read on one sample), and 停止全部
freezes without resetting (`moving == 0` while `away` is not 0). Two of those are waits for a reason: a row's first
frames can be bit-identical to its declared start — `ColorSampler` truncates to bytes, and some adapters write the first
frame inline in the click handler — so "how many rows have left their start" read at one instant is a sampling
accident, not a verdict. That exact mistake made the Jalium port flake 2 runs in 5.

Every row is also checked for **reachability** before anything is clicked: the driver brings it into view and then
asserts it is inside the window. The scroll is a request that some platforms serve asynchronously (MAUI's
`ScrollToAsync`, WinUI's `StartBringIntoView`), so the driver waits for the control to actually arrive before that
assertion — same mistake, same fix. A row nobody could reach still fails, which is the point: UIA's Invoke pattern
activates a control that was never laid out on screen exactly as happily as one that was, and that is how a layout
regression slips through.

### The same click, a second payload: what a real animation leaves on the control

Five frames driven one at a time verify a sampler's **arithmetic**. They cannot see what the library does with that
sampler when it drives it for real — the scheduler, the effect, endpoint normalization, resolving an interpolator by
property type, and the per-frame hop onto the UI thread. So the same click that publishes `over.conf` also starts a
real `Transition` on the same on-screen control and publishes what the control held while it ran, on `over.live`:

```text
v=1;seq=<n>;done=1;sampler=<name>;type=<TypeName>;k=<components>;
samples=<n>;bad=<n>;err=<message or ->;
last=…;min=…;max=…;
```

**Why this is not redundant.** The sampling loop's
`catch { effect.InvokeCancled(target, Args); }` (`TransitionInterpreterCore`) swallows anything a sampler throws, so a
sampler producing a value the framework will not take stops the animation where it stands — no exception, no event, no
readout. The one thing left behind is the element sitting short of its target, and that is exactly what `last` reports.

The scan is deliberately narrow and model-free. It does not re-state the curve; it asserts what a transition has
whatever curve it draws:

| Check | What it catches |
|---|---|
| `err` empty | `Execute`/`Prepare` threw before the animation started |
| `type` equals the table's | the control no longer holds the type the animation declared |
| `bad == 0` | a sample was NaN or ±Inf — never a legitimate overshoot |
| `samples >= 2` | the pipeline resolved no sampler for the path, or nothing landed on the control |
| `last` equals `Expected(1.0)` | **the animation stopped short** — the swallowed-exception case above, or the final queued frame never arriving |
| `LiveContract` bounds | a saturating type overshot its range instead of stopping at the bound |
| the envelope moved | the table says the value changes from t=0 to t=1 but the control never did |

`last` is compared against `ConformanceEntry.Expected(1.0)`, **not** against the endpoint the demo declared. Three
Avalonia samplers are discrete and hold their start value across the whole grid, so the declared endpoint is not where
they end; the table is the authority for all of them.

**How it is sampled, and what that cannot see.** The demo samples the control's property on a ~16 ms timer for the
duration of the run. It cannot be hooked to the property's change notification, because for a reference-typed product
the sampler rewrites *the same scratch instance* every frame and the framework raises no change for an identical
reference — that callback would fire once. So `min`/`max` are sampling-rate envelopes, not per-write ones: an
excursion shorter than a frame is missed. `last` is not a sample — it is read once the value has stopped changing, so
the endpoint check above is exact.

The run is one forward pass, no auto-reverse, so the final frame is exactly the endpoint. "Finished" is not a sleep:
the demo polls the settled value and publishes `done=1` once it has been unchanged for ~480 ms, and the suite waits for
that rather than for a fixed pause. A run whose value never settles is reported like any other anomaly.

**That detector infers completion from the value, which is weaker than it looks, and it has already been wrong once.**
The window was five frames (~80 ms) until a watchable-pace run at `VELOXDEV_BENCH_MS=1600` failed on Blazor — twice in
three runs, always with the identical settled value `rgba(240, 180, 50, 0.251)`, a 25-character near-end product where
the endpoint is the 9-character `#F0B43240`. The cause is **quantization**: Blazor's product is a CSS colour string
formatted to three decimals, and Back flattens out near the end, so several consecutive frames format to the same
string. "The value stopped changing" therefore becomes true while the animation still has a frame to deliver. A longer
animation makes it likelier — more Playwright polling on the circuit, sparser frames — which is why the default
800 ms bench had never shown it.

Thirty frames (~480 ms) is measured, not guessed: at the failing configuration the 5-frame window was wrong 2 of 3
runs and the 30-frame window was right 3 of 3. The residual risk is unchanged in kind — a stall longer than the whole
window, landing mid-animation, would still publish a mid-flight value and be reported as "stopped short" — but the bar
is now high enough that the observed failure mode is covered. A false report here names the sampler and both values,
so it is diagnosable rather than silent.

**The better fix is available and not taken yet.** `WeakDelegate.Clone()` copies handlers that are still alive, so a
`Completed` handler held in a field *does* fire on the cloned effect — the demos' comment claiming otherwise is wrong;
what actually kills such a subscription is that `WeakDelegate` holds the handler weakly, so a lambda with no strong
reference is collected. That is an exact completion signal, and on every platform `apply` has already landed by the
time it fires. Switching to it means getting the completion callback back onto the UI thread on six platforms (it runs
on the interpreter's thread), unless the flag is simply read by the tick that is already there — which is the shape
worth doing.

`String` is exempt from the envelope-moved check. Its payload carries character code points, which is an encoding
rather than a vector of quantities, and the sequence is a different length at different points of the run — minimum
against maximum has no answer there. Blazor is the only platform with a string product.

**What this still cannot prove.** That a demo drives its bench through the library at all. A hand-rolled loop writing
the same values would be indistinguishable from the outside, and this suite is about what the control is left holding,
not about which code put it there.

### The coverage guard, and why the expectation is not enough on its own

A demo that quietly stops offering one of its samplers would otherwise leave that sampler unverified while the suite
stayed green. Driving from the table makes the guard implicit and strict: the table names a handle, the driver clicks
it, and a handle that is not there is a failure that names it. A demo whose probe table and handle strip disagree
cannot compile around it either — the strip is generated from the table.

The check is deliberately falsifiable, and it has been exercised: breaking a single endpoint in a table fails with the
offending times named, and removing one handle from a demo fails with
`激活 X 失败：Waited 5.0s for a control with AutomationId 'over.sampler.X'`.

So has every live-animation check, one at a time, against the WPF demo — a failed run that reports nothing is the one
outcome that would make the whole payload pointless:

| Sabotage | What goes red |
|---|---|
| the demo's `Read` returns a NaN for one component | `真动画期间有 N 次采样拿到非有限值` **and** the endpoint mismatch |
| the effect is given `IsAutoReverse = true` | `真动画跑完后控件持有 [start]…，而 t=1 应当是 [end]` |
| a bound is tightened in `LiveContract` | `真动画的终值第 i 个分量是 X，超出…允许的…` |
| `over.live` is renamed in the markup | `这个 demo 没有 over.live…` — one line, not twelve |

Two of those runs also found real defects in the harness: `Math.Abs(NaN - x) > Tolerance` is *false*, so a NaN
component had been comparing as a match in the closed-form half too (`Matches` now checks finiteness first), and a
NaN value never compares equal to itself, so the settled-value detector never settled and every run burned its full
cap — the fast run went from 6s to 29s until `LiveWatch` learned to treat two NaNs as unchanged.

## The other half: what the animation pipeline does

A second suite drives the row that loads three shapes five different ways — on the UI thread or a background one,
exclusively or concurrently — plus repeat, reset and stop-all. Each demo publishes the three shapes' observable state
(`r0.*`, `r1.*`, `r2.*`) and `nomutual=<n>` on the same readout tick it already writes, and exposes the seven buttons
under tokens fixed identically on every platform (`over.btn.load.main`, `.background`, `.main.concurrent`,
`.background.concurrent`, `.repeat`, `over.btn.reset.all`, `over.btn.stop.all`).

**`nomutual` is the load-bearing field.** It is the number of *concurrent* runs still registered, read from
`TransitionSchedulerCore.TryGetNoMutualScheduler(target, out var arr)` — **the array's length, not the boolean**, because
the table entry outlives the runs. It is the only observable that distinguishes a concurrent load from an exclusive
one; without it that claim would collapse to "something moved".

**Every case here is model-free, deliberately.** The library offers no completion signal — `Execute` is `async void`,
the effect's events fire on an internal clone rather than on the builder's effect, and the mutual scheduler is cached
per target for its lifetime rather than being a liveness flag. Meanwhile all twenty-one of these animations
auto-reverse and loop, and no two platforms' are alike, so computing where one *should* end would mean re-implementing
the library. Each case therefore asserts a property that holds whatever the animation is:

- it moves when loaded (main thread, and background thread — not a duplicate: `Prepare` reads start values through a
  **blocking** `Dispatcher.Invoke`, so off-thread `Execute` has a real deadlock surface);
- a concurrent load registers concurrent runs and an exclusive one does not;
- stop freezes it exactly where it was (`Exit` never jumps to the end);
- reset returns it **exactly** to the state the demo declares, and keeps it there — which is also the only
  end-to-end check of the library's "a frame queued before a cancel must be dropped when it lands" guarantee.

**What is deliberately *not* asserted:** that a mutually-exclusive load cancels the one before it. There is no handle,
no counter, and `TryGetMutualScheduler` is not a liveness probe, so nothing outside the app can distinguish it; a case
that clicked twice and checked the landing would just be the same claim twice.

The declared rest values in `LoadMode/<Platform>LoadMode.cs` are transcribed from each demo's own reset path. Comparing
a reset against what the reset itself produced would be circular.

### A known caveat on Jalium

The reachability guard compares a control's UI Automation rectangle against the window's. On Jalium those two
rectangles disagree with where the pixels actually are: at 150% display scaling the reported rectangles for the
sampler handles sit tens of pixels below their drawn position, and a handle's reported bottom can fall past the
window's reported bottom while the control is plainly on screen. Verified by capturing the window's own content —
everything is visible — so this is Jalium's automation geometry, not a layout fault.

The practical consequence is that the guard's verdict on Jalium is unreliable in both directions, and on this machine
it currently passes only because the rectangles still overlap. It is not tightened to full containment for that
reason: doing so would fail Jalium spuriously on a UI nobody has a problem with. **Jalium's on-screen claims still
need a screenshot behind them.**

## Every registered platform must have a case that runs it

Two guards reflect over the suites and compare each `ConformanceCatalog`/`LoadModeCatalog` registry against the
platforms that actually have a `[TestCategory("AT.<platform>")]` method. This is not belt-and-braces: one case was lost
by an edit that used the case above it as its anchor and did not put it back, and nothing went red — the platform
simply stopped being verified while the suite stayed green.

## Layout

| Path | What lives there |
|---|---|
| `Engine/` | Payload parsing (`StatePayload`, `ConformancePayload`, `LivePayload`, `BatchPayload`), polling, the job object, the desktop process host, and the browser host. No UI framework, no FlaUI; `BlazorHost` is the **only** file that knows Playwright exists. |
| `Drivers/` | One driver per platform, plus the shared launch/readiness ordering. `AutomationLocator` is the **only** file that knows FlaUI exists. |
| `Conformance/` | One closed-form table per adapter, the arithmetic they are built from, and `LiveContract` — the per-type ranges a saturating sampler promises to stay inside. |
| `Suites/` | The launch probes and the conformance suite. |

### Adding a platform

Write its `SamplerProbe` in the demo, register its table in `ConformanceCatalog`, and add one test method to
`SamplerConformanceSuite`. The driver needs a launch path and the tokens its surface names controls by; launch,
readiness and payload reading are inherited.

The demo also has to publish `over.live` and `over.batch`, and have one of its handles start a real transition — a
platform whose demo only writes `over.conf` gets one failure saying so, rather than being quietly half-covered.
`SamplerProbe` in each demo carries the pieces (`Measurement`, `Property`/`Start`/`End`/`Create`, `LiveWatch`); they are
duplicated per demo on purpose, the way `SamplerSubject` already is, because a demo is meant to be readable and copyable
on its own.

The demo side of the row list has four traps that every port hit, so they are worth knowing before writing one:

1. **A visual can only have one parent.** Two rows cannot share one element — which is why the two displacement
   overshoot cases, which want to run on the same target so the curves can be compared, each get their own target in
   adjacent rows instead.
2. **Every scenario's travel was written for a whole window**, because those shapes used to sit loose on one. Moved
   into a row's stage they leave it, and an unclipped shape does not merely vanish — it slides across the neighbouring
   rows' text. Each element goes in a **clipped stage sized to its travel**, and where a stage would have to be absurd
   the *travel* is shrunk instead. WPF's load animation went 800 → 200; MAUI's needed a 420-wide stage because its 3D
   projection magnifies the same travel.
3. **Rotation and scale about a corner leave the stage.** Set the transform origin to the element's centre. (WinUI
   additionally needs a taller stage, because a rotating square's diagonal exceeds its side.)
4. **Do not delete a scenario to make a row fit.** One port removed a `Rotate3DTransform` for that reason and it had to
   be put back — its own centring fix had already solved the invisible-at-the-peak problem, and removing it silently
   dropped what that case exercised.

Two more that are not about geometry: the bulk start must not reuse a sampler row's own 启动 (it stops the
previously-watched row), and platforms where focus does not scroll — WinUI, MAUI — need the demo to bring a focused row
into view itself, or the reachability pass is red for every row below the fold.

Note that `DemoCatalog.Registry` and `ConformanceCatalog` are separate registers, and a platform in one but not the
other is exactly the silent gap the coverage guard exists to prevent.

## The demos' own surface

`over.state`, `over.readout` and the overshoot buttons are the demo's own: they are not part of this contract and this
project no longer reads them beyond using `over.state`'s advancing sequence number as the readiness handshake. A
readout is only trusted once its sequence has been seen to move, which is the one sound proof that the demo is live —
and, on Blazor, that the interactive circuit came up at all.

## The browser platform

Blazor is the one demo that needs no desktop, so it is the one that can run on a build agent. The suite starts the
demo's server itself and drives the page with Playwright, launching against the **installed Edge**
(`Channel = "msedge"`) so nothing has to be downloaded locally. A CI image without Edge installs the browser once and
drops the channel:

```bash
pwsh "Examples/Transition/AUTO TEST/bin/Debug/net10.0-windows/playwright.ps1" install chromium
```

Four things about that host are load-bearing, and all four fail loudly rather than quietly:

- The server is given `ASPNETCORE_URLS=http://127.0.0.1:5120` and `ASPNETCORE_ENVIRONMENT=Development`, and the suite
  navigates to that URL. `launchSettings.json` only applies to `dotnet run`, and the build output is not the published
  output: static web assets — including the framework's `blazor.web.js`, without which the circuit never starts and the
  readout sits at its prerendered `seq=0` forever — are only loaded in Development.
- With the URL supplied that way there is no https port for `UseHttpsRedirection` to redirect to, and the suite asserts
  the final URL still starts with `http://` so that a redirect is a failure rather than a different page being measured.
- Port 5120 has to be free. A server left over from an aborted run would be the page the suite measures, so the host
  refuses to start when something is already answering, and kills its own server's process tree on the way out.
- The `seq` handshake is what covers the SignalR circuit race: anything that lands before the circuit is live is
  silently dropped. It is not replaceable by a sleep.

## Filters

VSTest expression syntax (`--filter`):

- `TestCategory=AT.WPF` — one platform's suites.
- `FullyQualifiedName~EverySamplerMatchesItsClosedForm` — every platform's conformance test.
- `FullyQualifiedName~ObservationSurface` — the launch probes.

Add `--logger "console;verbosity=detailed"` to see measured values; passed tests are silent otherwise.
