# VeloxDev.AT — acceptance tests

Drives the transition demos through their real user interface and checks what they animate against an independent
model of the curves. It answers a question the backend suite cannot: *does the shipped demo, in a real window, driven
by a real click, actually overshoot its target and still land on it exactly?*

## Why this project is not in `VeloxDev.slnx`

Deliberately absent. The solution is all `dotnet test` at the repository root resolves, so not being in it is what
keeps the root command backend-only — a guarantee, not a filter. Run this project by path.

## Running it

Build the demo first; the suites start its executable, they do not reference its project (a `ProjectReference` would
drag MAUI and WinUI workloads into a project that only ever talks to them over UI Automation).

```bash
dotnet build Examples/Transition/WPF/Demo/Demo.csproj -c Debug
dotnet build Examples/Transition/Blazor/Demo/Demo/Demo.csproj -c Debug

# one platform's UI suites
VELOXDEV_AT=1 dotnet test "Examples/Transition/AUTO TEST/VeloxDev.AT.csproj" --filter "TestCategory=AT.WPF"
VELOXDEV_AT=1 dotnet test "Examples/Transition/AUTO TEST/VeloxDev.AT.csproj" --filter "TestCategory=AT.Blazor"

# everything: the theory suite plus every registered platform's UI tests
VELOXDEV_AT=1 dotnet test "Examples/Transition/AUTO TEST/VeloxDev.AT.csproj"
```

`--filter "TestCategory=AT.WPF"` is the form this runner accepts. `TestCategory~AT.WPF` also selects correctly, but
`Category=AT.WPF` does **not** — that name is not recognised and the run reports no matching tests.

Without `VELOXDEV_AT=1` every UI test is reported as skipped and the run still exits 0:

```bash
dotnet test "Examples/Transition/AUTO TEST/VeloxDev.AT.csproj"
# 失败: 0，通过: 6（理论套件），已跳过: 7
```

## Environment variables

| Variable | Meaning |
|---|---|
| `VELOXDEV_AT` | `1` lets the UI suites run. Unset means they skip. |
| `VELOXDEV_AT_PLATFORMS` | Comma-separated subset, e.g. `WPF,Blazor`. Unset means all. |
| `VELOXDEV_AT_DEMO_ROOT` | Directory to resolve demo executables under, instead of the repository root. Point it at a publish output. |
| `VELOXDEV_AT_REPEATS` | How many times the repeated-run scenario fires. Default 5; use 20 to calibrate a distribution. |

The theory suite ignores all of them: it is arithmetic, it needs no desktop, and it is worth running anywhere.

## The browser platform, and the agent

Blazor is the one demo that needs no desktop, so it is the one that can run on a build agent. The suite starts the
demo's server itself and drives the page with Playwright, and it launches against the **installed Edge**
(`Channel = "msedge"`) so that nothing has to be downloaded to run it locally. A CI image without Edge installs the
browser once and drops the channel:

```bash
pwsh Examples/Transition/AUTO TEST/bin/Debug/net10.0-windows/playwright.ps1 install chromium
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
- The `seq` handshake is what covers the SignalR circuit race: a click that lands before the circuit is live is
  silently dropped. Two reads whose `seq` differs prove the readout timer is running, which proves the circuit is up.
  It is not replaceable by a sleep.

## Filters

VSTest expression syntax (`--filter`):

- `TestCategory=AT.WPF` — one platform's UI suite plus its probes.
- `FullyQualifiedName~Wpf_Elastic` — a single scenario, useful while calibrating.
- `FullyQualifiedName~RepeatedRun` — the repeated-run calibration.

Add `--logger "console;verbosity=detailed"` to see the measured values each scenario reports; passed tests are silent
otherwise.

## Layout

| Path | What lives there |
|---|---|
| `Theory/` | `OvershootCurve`, an independent implementation of the two curves, plus the bound a UI observation has to clear, and the tests that pin it. |
| `Engine/` | Payload parsing, polling, the job object, the desktop process host, and the browser host. No UI framework, no FlaUI; `BlazorHost` is the **only** file that knows Playwright exists. |
| `Drivers/` | One driver per platform. `AutomationLocator` is the **only** file that knows FlaUI exists. |
| `Suites/` | The assertions, split by what a failure means. |

### Adding a platform

Register one line in `DemoCatalog.Registry` and add a driver: an executable path, a payload version, the two tokens the
platform's surface names its controls by, and the scenario table. Launch, click, read, the readiness handshake, the poll
to the end of a run and the peak collection are all inherited. A platform that is reached some other way overrides
`CreateHost` instead of touching any of that — `BlazorHost` is the one that does, for a page in a browser.

## The two assertions, and why they are shaped that way

- **Peak ≥ bound.** A UI observer samples the curve on a timer and can miss the top of it, so the observable claim is
  a lower bound. `OvershootCurve.Threshold` derives it from the curve and subtracts the sampling loss the readout
  interval implies — and it *throws* rather than return a bound that does not exceed the target, because a bound below
  the target would pass whether or not anything overshot.
- **Settle == target, only when `done == 1`.** Waiting for "the value equals the target" is wrong and unsound: both
  curves cross their target again on the way back (Elastic seven times in 1.1s), so such a wait passes while the
  animation is still in flight. The demos derive `done` from the duration and stamp it independently of the value.

## Measured, on this machine

WPF demo at 40ms readout, `VELOXDEV_AT_REPEATS=20`:

| Scenario | Start | Peak | Settled | Bound | Margin |
|---|---|---|---|---|---|
| Back, 0→300, 900ms | 0.000 | 329.827 (10 runs: 329.864–330.000) | 300.000 | 328.922 | ≈ 0.9 — the tight one |
| Elastic, 0→300, 1100ms | 0.000 | 411.275 (40 runs: 393.980–411.926) | 300.000 | 371.338 | ≈ 23 at worst |
| Size, 80→220, 1100ms | 80.000 | 268.008 (10 runs: 268.346–272.193) | 220.000 | 253.291 | ≈ 15 |
| Color, `#3a6ea5`→`#8080d0` | `#3a6ea5` | `#8681d4` (11 runs: `#8681d4`/`#8781d4`) | `#8080d0` | n/a | every channel above its target |
| Brush, non-solid | `LinearGradientBrush` | n/a | `ImageBrush` | n/a | non-solid throughout |

Back is the tight bound and stays tight: its peak sits where the curve is nearly flat, so a 40ms grid barely misses it,
while Elastic peaks early and steeply and can miss 0.1353 of its peak. Tightening Elastic means shortening the demo's
readout interval — never loosening the assertion.

The brush scenario settles on an `ImageBrush`, not on the gradient it animates towards: the non-solid path cross-fades
through a rendered brush. That is why the suite asserts "non-solid throughout" rather than a brush type.

### Blazor, at 50ms readout

The Blazor demo's numbers are its own: the translate and the width both end on 220, and its sixth scenario is a colour
saturation rather than a non-solid fill, because the Razor adapter has no brush type (it animates a CSS string). Its
readout interval is 50ms, which is what its bounds are derived from.

| Scenario | Start | Peak | Settled | Bound | Margin |
|---|---|---|---|---|---|
| Back, 0→220, 900ms | 0.000 | 241.936 (9 runs: 241.914–242.001) | 220.000 | 240.744 | ≈ 1.2 — the tight one |
| Elastic, 0→220, 1100ms | 0.000 | 302.079 (20 runs: 282.599–302.081) | 220.000 | 254.922 | ≈ 28 at worst |
| Size, 60→220, 1100ms | 60.000 | 274.858 | 220.000 | 245.398 | ≈ 29 |
| Color, `#3a6ea5`→`#8080d0` | `#3a6ea5` | `#8782d4` | `#8080d0` | n/a | every channel above its target |
| Brush (saturation), `#3a6ea5`→`#f6e68c` | `#3a6ea5` | red 255 (boundary, never below 58) | `#f6e68c` | n/a | the channel stops at the limit instead of wrapping |

A frame of a colour scenario reads `rgba(r, g, b, a)` and only the two endpoints are the `#rrggbb` the caller passed
in, so those assertions parse both spellings and are made per channel. The saturation scenario is the substitution for
the brush one: the target puts the red channel past 255, so the shared progress has to stop at the boundary — a bare
byte cast would turn 263 into 7 — while green overshoots its target and blue passes below its own.
