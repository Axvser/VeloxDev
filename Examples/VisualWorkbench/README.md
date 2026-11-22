# Velox Visual Workbench

A single-window demo for the HWND-free visual backend of `VeloxDev.WinForms`.

`VisualSurface` is one WinForms `Control` (one HWND) that owns a tree of `VisualElement` objects under
`VeloxDev.WinForms.Xaml.Visuals.Controls`. The tree is measured, arranged, hit-tested and painted in one
pass, so `Border`, `StackPanel`, `TextBlock`, `Image`, `Button`, `TextBox` and `ScrollViewer` create no
windows of their own. Pointer, keyboard and text input are routed to the logical focused element.

The window opens on the first non-primary `Screen` when one exists, so it does not cover the main
screen; `ShowWithoutActivation` keeps it from taking focus on open. It falls back to the primary screen
when there is only one.

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

## Native islands (airspace)

`NativeControlHost` hosts a real WinForms control as a child HWND above the surface. WinForms child
windows always paint above the visual tree, so an island cannot be clipped by a `ScrollViewer`, cannot
be transformed or rotated with a visual, and cannot participate in group opacity or effects. Use it
only where native behaviour is required (text editing, third-party hosts); keep everything else in the
visual tree.
