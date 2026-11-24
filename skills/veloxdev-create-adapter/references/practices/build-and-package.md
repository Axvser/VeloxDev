# Build, package and wire

## When an adapter is the right answer

| Situation | Do |
| --- | --- |
| Model and engine only, headless | Reference `VeloxDev.Core` alone; no adapter. |
| The GUI is one of WPF, Avalonia, WinUI, MAUI, WinForms, Blazor/Razor or Jalium | Use the shipped adapter. |
| A GUI with no official adapter, but XAML-like and with a scroll viewer + canvas | Write a full adapter. |
| You want the canvas inside a control you already own | Still a full adapter; the surface is not separable from the coordinate host. |
| You will build this GUI more than once | Add an item-template pack after the demo works. |

An adapter is a real cost. The seven official adapters cover most .NET GUIs; derive a new one only when they do not.

## Package layout

Two independent packages, two projects:

```text
Src/Adapters/VeloxDev.<GUI>/                 adapter
  VeloxDev.<GUI>.csproj
  TransitionSystem/          UIThreadInspector, Interpolator, scheduler, interpreter, State, samplers
  DynamicTheme/              ThemeValueConverters
  WorkflowSystem/
    AttachedBehaviors/       surface, node drag, slot connection, slot layout, events, transform, minimap
    ViewHosting/             ViewPool host layers, minimap layers

Src/Templates/VeloxDev.<GUI>.Templates/
  working/VeloxDev.<GUI>.Templates.csproj    netstandard2.0, PackageType=Template
  working/content/<role>/.template.config/{template.json,dotnetcli.host.json}
```

⚙ **The template pack is a separate package and a separate build.** It is a `netstandard2.0` project with `IncludeBuildOutput=false`, `IncludeContentInPack=true`, `EnableDefaultCompileItems=false` and `None Include="content\**\*"`. It carries no runtime assembly; it ships the seven role templates. `dotnet new install VeloxDev.<GUI>.Templates` makes the short names (`wpf-v-tree` style) available.

⚙ **Match the adapter's TFMs to the host.** A WPF adapter, for example, targets `netframework4.6.1;net5.0-windows;netcoreapp3.0;net8.0-windows`; Core targets `netstandard2.0;netframework4.6.1;net5.0;netcoreapp3.0;net8.0`. Choose the intersection your framework supports and keep the oldest target the users need.

⚙ **A package reference for Core in Release, a project reference in Debug.** The shipped adapters use:

```xml
<ProjectReference Include="../../Core/VeloxDev.Core/VeloxDev.Core.csproj"
                  Condition="'$(Configuration)' == 'Debug'" />
<PackageReference Include="VeloxDev.Core" Version="<version>"
                  Condition="'$(Configuration)' != 'Debug'" />
```

The generator analyzer cannot flow transitively; a project that uses `[VeloxProperty]` / `[VeloxCommand]` / `[WorkflowBuilder.*]` needs its own Debug project reference and Release package reference pair, exactly as Core does.

⚙ **`VeloxDevLocalCore=true` swaps package references for the working tree.** It is a build-only release aid defined in `Src/Directory.Build.targets`; it applies to projects that actually consume `VeloxDev.Core` or `VeloxDev.Core.Generator`. Do not depend on it for a normal local build.

## Namespaces

| Namespace | Contains |
| --- | --- |
| `VeloxDev.WorkflowSystem` | model, geometry, input route, pipelines, minimap contracts, surface math |
| `VeloxDev.WorkflowSystem.StandardEx` | `WorkflowTreeEx`, `WorkflowNavigationEx`, standard operations |
| `VeloxDev.WorkflowSystem.AttachedBehaviors` | adapter's attached behaviors (WPF naming; other adapters follow the same idea) |
| `VeloxDev.WorkflowSystem.ViewHosting` | adapter's view-pool and minimap layer implementations |
| `VeloxDev.TransitionSystem` | transition API users name: `Transition<T>`, `TransitionEffect`, `Eases`, `PathIndex`, effects |
| `VeloxDev.TransitionSystem.Abstractions` | `InterpolatorCore`, `TransitionHostBase`, `TransitionInterpreterCore`, `TransitionSchedulerCore`, `SamplerSet` |
| `VeloxDev.Threading` | `NonPriority`, `ThreadRef`, `IThreadDispatcher<TPriorityCore>` |
| `VeloxDev.DynamicTheme` | `ThemeManager`, `ITheme`, `IThemeObject`, `IThemeValueConverter`, `[ThemeConfig]` |

## Wiring the surface

The generated tree view composes platform primitives and turns the adapter on by name. The WPF shape, which the other attached-property adapters mirror:

```xml
<UserControl xmlns:behaviors="clr-namespace:VeloxDev.WorkflowSystem.AttachedBehaviors;assembly=VeloxDev.WPF"
             behaviors:WorkflowSurfaceBehavior.IsEnabled="True"
             behaviors:WorkflowSurfaceBehavior.ZoomEnabled="True"
             behaviors:WorkflowSurfaceBehavior.ScrollViewerName="PART_ScrollViewer"
             behaviors:WorkflowSurfaceBehavior.CanvasName="PART_Canvas"
             behaviors:WorkflowSurfaceBehavior.GridDecoratorName="PART_GridDecorator"
             behaviors:WorkflowSurfaceBehavior.PointerPressSourceName="PART_SurfaceBorder"
             behaviors:WorkflowSurfaceBehavior.MinimapOverlayName="PART_MinimapOverlay"
             behaviors:WorkflowSurfaceBehavior.LinkMenuKey="LinkContextMenu">
  …
  <Canvas x:Name="PART_Canvas"
          Width="{Binding Layout.ActualSize.Width}"
          Height="{Binding Layout.ActualSize.Height}"
          behaviors:ViewPool.ItemsSource="{Binding Helper.VisibleItems}"
          behaviors:ViewPool.TemplateSelector="{StaticResource WorkflowTemplateSelector}" />
</UserControl>
```

The node view turns on slot measurement and drag:

```xml
<UserControl behaviors:WorkflowSlotLayoutBehavior.IsEnabled="True"
             behaviors:WorkflowSlotLayoutBehavior.SlotNames="PART_InputSlot"
             behaviors:WorkflowSlotLayoutBehavior.SlotEnumeratorNames="PART_OutputSlots"
             behaviors:WorkflowSlotLayoutBehavior.CoordinateHostName="PART_Canvas">
```

and names the same coordinate host on `WorkflowNodeDragBehavior`. A slot view turns on `WorkflowSlotConnectionBehavior`; a component view optionally binds `WorkflowEvents.Node` / `.Slot` / `.Tree` to an event sink.

⚙ **The names are the contract.** A missing `PART_` name disables that part silently. If you rename one, update the attached property in the same edit.

⚙ **The surface must be focusable.** On WPF/`UserControl`, `Focusable` defaults to `false`; the adapter sets it. Without a focusable host (or a focusable per-link view) the Delete key has no route.

⚙ **`ViewPool.ItemsSource` is `ITreeHelper.VisibleItems`.** The pool materializes one view per visible item through the template selector; a single-overlay link platform returns `true` from `IViewLinkLayer.Handles` and draws links itself instead of materializing one view per link.

## Registration at startup

```csharp
protected override void OnStartup(StartupEventArgs e)
{
    base.OnStartup(e);

    ThemeManager.SetPlatformInterpolator(new Interpolator());
    ThemeManager.StartModel = StartModel.Cache;
    ThemeManager.SetCurrent<Dark>();

    new MainWindow().Show();
}
```

⚙ **`SetPlatformInterpolator` once, before any element registers or any switch starts.** It runs the adapter's sampler registrations and is what makes `CreateScheduler` reachable. Without it, a switch still happens — instantly.

⚙ **`SetCurrent` before elements initialize.** It decides which theme their initial values come from.

⚙ **The adapter's `Interpolator` is the static registration point.** Its static constructor registers only the framework's value types; `CreateScheduler` returns `FindOrCreate(target)` for the platform's effect type and `null` otherwise.

## Build order for the adapter itself

1. `UIThreadInspector : TransitionHostBase<TPriorityCore>` — `ThreadFor`, `IsCurrentThread`, `PostCore`, and `InternalPriority` when the priority is not `NonPriority`.
2. `Interpolator : InterpolatorCore` — samplers for the framework's value types, `CreateScheduler`.
3. Interpreter + frame pacer; `State`; the public `TransitionScheduler` / `Transition` / `TransitionEffect` closures.
4. Surface behavior: named-part resolution, `ISurfaceMetricsSource`, `SurfaceScrollerCore` host, `ISurfaceViewportRestoreHost`, pan, wheel, input routing, focus.
5. Slot layout: `ISlotPartLocator` over the visual tree and `ISlotMeasurementHost` over the UI thread.
6. Node drag and slot connection behaviors.
7. Link view: `IsRenderReady()`, `LinkCurve`, `PublishCurve`.
8. Grid decorator and minimap: implement the Core interfaces, then compose `MinimapPipelineCore` from renderer/viewport/input layers.
9. Theme converters.
10. The tree view wiring and, optionally, the item-template pack.

The platform-specific decisions this order depends on — coordinate host, scale container, slot-anchor function, link strategy, zoom hook, priority type and change model — are spelled out in [new-adapter.md](../../../veloxdev-create-workflow/references/practices/new-adapter.md#the-seven-decisions). The sampler, priority and pacer contracts in full are in [the animation adapter reference](../../../veloxdev-create-animation/references/practices/adapter.md).

## Validation

Build the demo before the template pack. The demo is where the behavior settles; the pack is a transcription of it.

```bash
# the demo for the GUI (names follow the platform, not always the adapter)
dotnet build "Examples/Workflow/<GUI> Trimmed/Demo/Demo.csproj" -c Debug
```

Minimum end-to-end proof:

- drag a node;
- drag a slot onto another slot and get a link;
- pan and zoom with the point under the pointer staying put;
- the ruler and minimap track;
- a graph round trip through `Serialize()` / `Deserialize()` restores positions and the viewport.

⚙ **Test at 40%, not 100%.** Vanished links, detached ports and drifting cameras are invisible at 100%.

⚙ **Run the headless surface contract.** Five adapters (WPF, Avalonia, WinForms, Razor, Jalium) have no-window suites; they assert named-part resolution, viewport writes, restore, `VisibleItems`, `ViewPool`, and detach behavior over one shared `TestGraph`:

```powershell
./Src/Test/Headless/run-headless-tests.ps1
# one adapter, while iterating
dotnet test Src/Test/Headless/VeloxDev.WPF.Headless.Tests/VeloxDev.WPF.Headless.Tests.csproj
```

⚙ **Run the real-window acceptance suite when the adapter is visual.** It drives the demos over UI Automation/Playwright, seven platforms, one window at a time:

```powershell
./Src/Test/AutoTest/run-autotest.ps1
# one platform, fast
VELOXDEV_AT=1 VELOXDEV_AT_PLATFORMS=WPF VELOXDEV_AT_PACE=0 VELOXDEV_AT_OBSERVE=0 VELOXDEV_BENCH_MS=200 `
  dotnet test Src/Test/AutoTest/VeloxDev.AT.csproj --nologo --filter "TestCategory=AT.WPF"
```

Adding a platform to the acceptance suite is one class in `Suites/PlatformSuites.cs` plus one line in `Drivers/DemoCatalog.cs`; `Src/Test/AutoTest/AGENTS.md` is the operating guide. A headless adapter can add a sibling suite under `Src/Test/Headless/` and keep the surface contract tests green.

## Package checklist

- [ ] Adapter and template package versions tracked independently from Core (see the version-management specification).
- [ ] Debug uses `ProjectReference`; Release uses `PackageReference` for Core and the generator.
- [ ] The generator analyzer reference is repeated in every project that uses its attributes.
- [ ] `ThemeManager.SetPlatformInterpolator` is called once at startup.
- [ ] Every `PART_` name used by the surface/slot/node behavior exists in the generated markup.
- [ ] The slot layout registers each port the model can connect; an unregistered port stays `NaN`.
- [ ] The link view calls `IsRenderReady()` and publishes the curve it draws.
- [ ] The template pack contains all seven roles and each `template.json` names a `shortName`.
- [ ] The demo passes deep-zoom, connection and serialize/deserialize checks.
