# XamlGallery

The living catalogue for the VeloxDev WinForms **visual backend** (`VeloxDev.WinForms.Xaml.Visuals`).
The whole window is built from one `VisualSurface` — a single HWND — and a tree of visual elements;
no card creates a child window. Native WinForms controls are not bridged: the only way to use one is
an explicit `NativeControlHost` island.

The window is a three-pane workbench:

- **Catalog** (left): a visual `TreeView` of the feature groups and their cards, with a filter box.
- **Preview** (center): the selected card plus its `Usage` snippet.
- **Properties** (right): the editable dependency properties of the card's visual root, discovered by
  reflection; strings, numbers, booleans, enums and colours are editable.

The shell itself is visual too: title bar, docked panes, `GridSplitter` sashes, tree, scroll viewers
and status bar are all `VisualElement`s.

## Run

```bash
dotnet run --project Examples/XamlGallery/Demo/Demo.csproj
```

Useful switches:

```bash
# open and close the window without interaction
dotnet run --project Examples/XamlGallery/Demo/Demo.csproj -- --smoke

# open one entry, a group, or the light palette
dotnet run --project Examples/XamlGallery/Demo/Demo.csproj -- --entry layout.stack-panel
dotnet run --project Examples/XamlGallery/Demo/Demo.csproj -- --group data --light

# scroll the preview by pixels
dotnet run --project Examples/XamlGallery/Demo/Demo.csproj -- --entry data.tree-view --scroll 200

# capture one card to a PNG and dump the visual tree
dotnet run --project Examples/XamlGallery/Demo/Demo.csproj -- --capture ./captures --entry layout.stack-panel --dump

# capture every card
dotnet run --project Examples/XamlGallery/Demo/Demo.csproj -- --capture ./captures --all
```

`--capture` places the window on the first non-primary screen when one exists, selects each target in
catalog order, waits for layout, renders the window into a bitmap with `PrintWindow` (falling back to
`VisualSurface.RenderTo`), and closes. `--dump` writes a visual tree with bounds and colours next to
each PNG. `--preview-only` hides the title, side panes and status strip for a content-only capture.
`--light` starts the process in the Light+ palette.

## Cards

| Group | Shows |
| --- | --- |
| Layout | `StackPanel`, `DockPanel`, `Grid`, `WrapPanel`, `Canvas`, `UniformGrid`, `Viewbox`, `GridSplitter`, `GroupBox`, `Expander`, `TabControl` |
| Text | `TextBlock`, `TextBox`, password/multiline editing, `RichTextBox`, `Image` |
| Data | `ListBox`, `ComboBox`, `VirtualList`, `TreeView`, `Calendar`, `DatePicker` |
| Input | `Button`/`RepeatButton`, toggles, `Slider`, `ProgressBar`, `NativeControlHost` |
| Graphics | Shapes (`Rectangle`, `Ellipse`, `Polygon`, `Path`, `Line`), `VisualPresenter` with a lightweight `Visual` tree, opacity and clip |
| Overlay | `VisualPopup`, `VisualMenu`, `VisualContextMenu` |
| Styling | Implicit/explicit `VisualStyle`, `VisualTrigger`, `VisualControlTemplate` with `ContentPresenter` and a named part, resources |
| Binding | One-way/two-way path bindings, inherited `DataContext`, a converter |
| Theme | Palette swatches, typography, palette rebuild |
| Visuals | A generated `v:` XAML view and a real `WorkflowTree` graph on the visual backend |

## Notes

- One `GalleryModel` is shared by every card; data cards bind to its sample collections and values.
- Cards are factories in `GalleryVisualCards.cs`: selecting a card builds a fresh visual tree and
  disposes the previous one.
- Theme switching rebuilds the visual root so every control reads the new `Theme.Current` values.
- The inspector writes local dependency-property values on the card root; it does not edit template
  or child parts.
- The workflow graph card is a demo-level surface (drag/pan/zoom/Delete); the reusable
  `VisualWorkflowSurface` host is used by both `Examples/Workflow/WinForms` and
  `Examples/Workflow/WinForms Trimmed`.
- Platform-wall controls (MediaElement, WebBrowser, InkCanvas, Viewport3D) are intentionally absent.
