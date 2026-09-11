# VeloxDev.AT — sampler conformance, in a real app

Drives each transition demo and checks, **inside the running application**, that every sampler the platform's adapter
ships produces exactly what an independently written closed form says.

It answers a question a headless suite cannot: a sampler whose product is a framework object — a brush, a shadow, a
transform, a projection — needs a live runtime even to build its endpoints, so the only place it can be driven is an
app that already has one. The demo does the driving; this project holds the expectation.

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
| `VELOXDEV_AT_PACE` | Milliseconds to linger after each clicked handle lands. **Defaults to 800** so a run can be watched and so each sampler's bench playback finishes before the next click interrupts it; set `0` for the fast run an agent wants (~20s for all seven platforms). |

## What is checked

Each demo presents **one handle per sampler** its adapter ships, carrying the automation token
`over.sampler.<SamplerTypeName>`. The suite clicks each handle in turn and reads what that click published on the
element carrying `over.conf`:

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

### The coverage guard, and why the expectation is not enough on its own

A demo that quietly stops offering one of its samplers would otherwise leave that sampler unverified while the suite
stayed green. Driving from the table makes the guard implicit and strict: the table names a handle, the driver clicks
it, and a handle that is not there is a failure that names it. A demo whose probe table and handle strip disagree
cannot compile around it either — the strip is generated from the table.

The check is deliberately falsifiable, and it has been exercised: breaking a single endpoint in a table fails with the
offending times named, and removing one handle from a demo fails with
`激活 X 失败：Waited 5.0s for a control with AutomationId 'over.sampler.X'`.

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
| `Engine/` | Payload parsing, polling, the job object, the desktop process host, and the browser host. No UI framework, no FlaUI; `BlazorHost` is the **only** file that knows Playwright exists. |
| `Drivers/` | One driver per platform, plus the shared launch/readiness ordering. `AutomationLocator` is the **only** file that knows FlaUI exists. |
| `Conformance/` | One closed-form table per adapter, and the arithmetic they are built from. |
| `Suites/` | The launch probes and the conformance suite. |

### Adding a platform

Write its `SamplerProbe` in the demo, register its table in `ConformanceCatalog`, and add one test method to
`SamplerConformanceSuite`. The driver needs a launch path and the tokens its surface names controls by; launch,
readiness and payload reading are inherited.

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
