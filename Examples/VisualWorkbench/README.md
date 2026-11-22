# Velox Visual Workbench

A single-window demo for the HWND-free visual backend of `VeloxDev.WinForms`.

`VisualSurface` is one WinForms `Control` (one HWND) that owns a tree of `VisualElement` objects under
`VeloxDev.WinForms.Xaml.Visuals.Controls`. The tree is measured, arranged, hit-tested and painted in one
pass, so `Border`, `StackPanel`, `TextBlock`, `Image`, `Button`, `TextBox` and `ScrollViewer` create no
windows of their own. Pointer, keyboard and text input are routed to the logical focused element.

The window opens on the first non-primary `Screen` when one exists, so it does not cover the main
screen; `ShowWithoutActivation` keeps it from taking focus on open. It falls back to the primary screen
when there is only one.

## What the window shows

- **Visual XAML** — a `v:*` view compiled by the WinForms XAML generator.
- **Styles** — implicit visual styles and priority layers.
- **Selector** — a visual `ComboBox` whose drop-down opens in the surface overlay (no second HWND).
- **Templates** — a `ContentControl` with a visual `ControlTemplate`, a named part, a `ContentPresenter`
  and a template trigger.
- **Graph** — a live `TreeDefaultComponent` workflow rendered by visual node cards and one link layer:
  drag a node, drag the canvas to pan, Ctrl+wheel to zoom, Delete removes the hovered link.

The graph section is deliberately demo-level: it reuses Core (`WorkflowInput`, `LinkCurve`, model
commands) and the public visual backend, but it does not pool views, draw a grid/ruler/minimap, or start
connection drags. The WinForms item templates and the `Examples/Workflow` demos still use the HWND
backend.

## Run

```powershell
dotnet run --project Examples/VisualWorkbench/Demo.csproj
```

## Capture

```powershell
dotnet run --project Examples/VisualWorkbench/Demo.csproj -- --capture C:\temp\workbench.png
```

Capture mode prints every `Screen.AllScreens` bounds, pumps a few frames, writes the PNG with
`PrintWindow` (`PW_RENDERFULLCONTENT`) and closes the window. If `PrintWindow` fails it falls back to
`DrawToBitmap`.

Additional capture switches, applied before the screenshot:

| Switch | Effect |
| --- | --- |
| `--light` | Start in the Light+ theme |
| `--scroll <px>` | Scroll the outer page |
| `--open-combo` | Open the first visual ComboBox |
| `--graph-drag` | Drag the first workflow node |
| `--graph-delete` | Hover the first link and press Delete |
| `--graph-zoom` | Zoom the workflow graph |

## Native islands (airspace)

`NativeControlHost` hosts a real WinForms control as a child HWND above the surface. WinForms child
windows always paint above the visual tree, so an island cannot be clipped by a `ScrollViewer`, cannot
be transformed or rotated with a visual, and cannot participate in group opacity or effects. Use it
only where native behaviour is required (text editing, third-party hosts); keep everything else in the
visual tree.
